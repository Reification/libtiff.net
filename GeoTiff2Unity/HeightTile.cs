using System;
using System.IO;
using System.Runtime.InteropServices;

namespace R9N {
	public static class IOUtil {
		public static byte[] StructArrayToByteArray<T>(T[] source, ref byte[] destination) where T : struct {
			GCHandle handle = GCHandle.Alloc(source, GCHandleType.Pinned);
			try {
				int byteCount = source.Length * Marshal.SizeOf(typeof(T));
				IntPtr pointer = handle.AddrOfPinnedObject();
				if (!(destination?.Length >= byteCount)) {
					destination = new byte[byteCount];
				}
				Marshal.Copy(pointer, destination, 0, byteCount);
				return destination;
			} finally {
				if (handle.IsAllocated)
					handle.Free();
			}
		}

		public static T[] ByteArrayToStructArray<T>(byte[] source, ref T[] destination) where T : struct {
			int instanceCount = source.Length / Marshal.SizeOf(typeof(T));
			if (!(destination?.Length >= instanceCount)) {
				destination = new T[instanceCount];
			}
			GCHandle handle = GCHandle.Alloc(destination, GCHandleType.Pinned);
			try {
				IntPtr pointer = handle.AddrOfPinnedObject();
				Marshal.Copy(source, 0, pointer, source.Length);
				return destination;
			} finally {
				if (handle.IsAllocated)
					handle.Free();
			}
		}

		public static byte[] StructArrayToByteArray<T>(T[] source) where T : struct {
			byte[] dst = null;
			return StructArrayToByteArray(source, ref dst);
		}

		public static T[] ByteArrayToStructArray<T>(byte[] source) where T : struct {
			T[] destination = null;
			return ByteArrayToStructArray<T>(source, ref destination);
		}

		public static byte[] StructToByteArray<T>(T source) where T : struct {
			byte[] destination = null;
			return StructArrayToByteArray(new T[] { source }, ref destination);
		}

		public static byte[] StructToByteArray<T>(T source, ref byte[] destination) where T : struct {
			return StructArrayToByteArray(new T[] { source }, ref destination);
		}

		public static T ByteArrayToStruct<T>(byte[] source) where T : struct {
			return ByteArrayToStructArray<T>(source)[0];
		}
	}

	public static class HeightTile {
		const uint kRowsPerStrip = 32;

		public static void Write<T>(string path, HeightTileHeader hdr, T[] samples) where T : struct {
			bool typeIsFloat = ((typeof(T) == typeof(float)) || (typeof(T) == typeof(double)));

			if (Marshal.SizeOf(typeof(T)) != hdr.bytesPerSample || typeIsFloat != (hdr.isFloat != 0)) {
				throw new Exception("invalid sample type for this header.");
			}

			if (samples.Length != (hdr.tileSizePix * hdr.tileSizePix)) {
				throw new Exception("source buffer size does not match header size.");
			}

			using (var outRaw = new FileStream(path, FileMode.Create, FileAccess.Write)) {
				hdr.Write(outRaw);

				uint pitch = hdr.tileSizePix * hdr.bytesPerSample;

				byte[] tmpStrip = new byte[kRowsPerStrip * pitch];

				for (uint y = 0; y < hdr.tileSizePix; y += kRowsPerStrip) {
					uint stripRowByteCount = Math.Min(kRowsPerStrip, hdr.tileSizePix - y) * pitch;

					Buffer.BlockCopy(samples, (int)(y * pitch), tmpStrip, 0, (int)stripRowByteCount);

					outRaw.Write(tmpStrip, 0, (int)stripRowByteCount);
				}
			}
		}

		public static HeightTileHeader Read(string path, ref float[,] samples) {
			var hdr = default(HeightTileHeader);
			byte[] rawTile = null;

			using (var inRaw = new FileStream(path, FileMode.Open, FileAccess.Read)) {
				hdr = HeightTileHeader.Read(inRaw);

				uint pitch = hdr.tileSizePix * hdr.bytesPerSample;

				rawTile = new byte[hdr.tileSizePix * pitch];

				inRaw.Read(rawTile, 0, rawTile.Length);
			}

			if (!(samples?.GetLength(0) == hdr.tileSizePix && samples?.GetLength(1) == hdr.tileSizePix)) {
				samples = new float[hdr.tileSizePix, hdr.tileSizePix];
			}

			if (hdr.isFloat != 0) {
				Buffer.BlockCopy(rawTile, 0, samples, 0, rawTile.Length);
			} else if (hdr.bytesPerSample == 2) {
				float hScale = 1.0f / ushort.MaxValue;

				for (int y = 0, rr = 0; y < hdr.tileSizePix; y++, rr += (int)(hdr.tileSizePix << 1)) {
					for (int x = 0, rx = 0; x < hdr.tileSizePix; x++, rx += 2) {
						float fh = (rawTile[rr + rx] + (rawTile[rr + rx + 1] << 8)) * hScale;
						samples[y, x] = fh;
					}
				}
			} else if (hdr.bytesPerSample == 1) {
				float hScale = 1.0f / byte.MaxValue;

				for (int y = 0, r = 0; y < hdr.tileSizePix; y++, r += (int)hdr.tileSizePix) {
					for (int x = 0; x < hdr.tileSizePix; x++) {
						float fh = rawTile[r + x] * hScale;
						samples[y, x] = fh;
					}
				}
			}

			return hdr;
		}
	}

	public struct HeightTileHeader {
		public static readonly uint kMagic = ((uint)Char.GetNumericValue('r') << 24) +
																					((uint)Char.GetNumericValue('9') << 16) +
																					((uint)Char.GetNumericValue('n') << 8) +
																					(uint)Char.GetNumericValue('h');
		public uint magic;
		public uint tileSizePix;
		public uint bytesPerSample;
		public uint isFloat;
		public uint posX;
		public uint posY;
		public float pixToMeters;
		public float minTotalTerrainHeight;
		public float maxTotalTerrainHeight;

		public float tileSizeMeters {
			get { return tileSizePix * pixToMeters; }
		}

		public float totalTerrainHeightRange {
			get { return maxTotalTerrainHeight - minTotalTerrainHeight; }
		}

		public void Init<T>(uint wh, uint px, uint py, float pixSize, float minV, float maxV) where T : struct {
			magic = kMagic;
			tileSizePix = wh;
			bytesPerSample = (uint)Marshal.SizeOf(typeof(T));
			isFloat = ((typeof(T) == typeof(float)) || (typeof(T) == typeof(double))) ? 1u : 0u;
			posX = px;
			posY = py;
			pixToMeters = pixSize;
			minTotalTerrainHeight = minV;
			maxTotalTerrainHeight = maxV;
		}

		public void Validate() {
			if (magic != kMagic) {
				throw new Exception(string.Format("incorrect magic number {0} expected {1}", magic, kMagic));
			}

			if (((tileSizePix - 1) & (tileSizePix - 2)) != 0) {
				throw new Exception(string.Format("Height map size {0} is invalid. Must be 2^N + 1.", tileSizePix));
			}

			if (isFloat > 1) {
				throw new Exception(string.Format("Invalid value for isFloat {0}. Must be 0 or 1.", isFloat));
			}

			if ((isFloat != 0) && (bytesPerSample != 4u)) {
				throw new Exception(string.Format("Invalid bytes per sample {0} for sample type float. Must be 4.", bytesPerSample));
			}

			if ((isFloat == 0) && (bytesPerSample != 1u) && (bytesPerSample != 2u)) {
				throw new Exception(string.Format("Invalid bytes per sample {0} for sample type uint. Must be 1 or 2.", bytesPerSample));
			}

			if (pixToMeters <= 0.0f) {
				throw new Exception(string.Format("Invalid pixel to meters scale {0}. Must be > 0", pixToMeters));
			}

			if (minTotalTerrainHeight < 0.0f || maxTotalTerrainHeight < minTotalTerrainHeight) {
				throw new Exception(string.Format("Invalid min/max total terrain heights {0}/{1}. Must be >= 0 and max must be >= min.", minTotalTerrainHeight, maxTotalTerrainHeight));
			}
		}

		public void ValidateCompatible(HeightTileHeader expected) {
			if (bytesPerSample != expected.bytesPerSample) {
				throw new Exception(string.Format("bytesPerSample {0} does not match expected {1}", bytesPerSample, expected.bytesPerSample));
			}

			if (isFloat != expected.isFloat) {
				throw new Exception(string.Format("isFloat {0} does not match expected {1}", isFloat, expected.isFloat));
			}

			if (pixToMeters != expected.pixToMeters) {
				throw new Exception(string.Format("pixSizeM {0} does not match expected {1}", isFloat, expected.isFloat));
			}

			if (minTotalTerrainHeight != expected.minTotalTerrainHeight || maxTotalTerrainHeight != expected.maxTotalTerrainHeight) {
				throw new Exception(string.Format("min/max total terrain heights {0}/{1} do not match expected {2}/{3}",
					minTotalTerrainHeight, maxTotalTerrainHeight, expected.minTotalTerrainHeight, expected.maxTotalTerrainHeight));
			}
		}

		public void ValidateExactMatch(HeightTileHeader expected) {
			ValidateCompatible(expected);
			if (tileSizePix != expected.tileSizePix) {
				throw new Exception(string.Format("tileSizePix {0} does not match expected tileSizePix {1}", tileSizePix, expected.tileSizePix));
			}
		}

		public static HeightTileHeader Read(FileStream inFile) {
			var thisBytes = new byte[Marshal.SizeOf(typeof(HeightTileHeader))];
			inFile.Read(thisBytes, 0, thisBytes.Length);
			HeightTileHeader hdr = IOUtil.ByteArrayToStruct<HeightTileHeader>(thisBytes);

			hdr.Validate();

			return hdr;
		}

		public void Write(FileStream outFile) {
			var thisBytes = IOUtil.StructToByteArray(this);
			outFile.Write(thisBytes, 0, thisBytes.Length);
		}
	}
}
