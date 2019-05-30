using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GeoTiff2Raw {
	public enum RasterRotation {
		CCW_90,
		CCW_180,
		CCW_270,
		CW_90 = CCW_270,
		CW_180 = CCW_180,
		CW_270 = CCW_90
	}

	public class Raster<T> where T : struct {
		public static readonly uint pixSize = sizeofPixType();

		public uint bitsPerPixel {  get { return pixSize * 8; } }

		public Raster() {
			width = height = pitch = 0;
			pixels = null;
		}

		public Raster(uint _width, uint _height) {
			Init(_width, _height);
		}

		public void Init(uint _width, uint _height) {
			width = _width;
			height = _height;
			pitch = _width * pixSize;
			pixels = new T[width * height];
		}

		public Raster<T> Clone() {
			Raster<T> clone = new Raster<T>(width, height);
			Array.Copy(pixels, 0, clone.pixels, 0, pixels.Length);
			return clone;
		}

		public Raster<T> Clone(uint x, uint y, uint w, uint h) {
			Raster<T> clone = new Raster<T>(w, h);
			uint dstRow = 0;
			uint srcRow = y * width + x;
			for (uint r = 0; r < h; r++) {
				Array.Copy(pixels, (int)srcRow, clone.pixels, (int)dstRow, (int)clone.width);
				srcRow += width;
				dstRow += clone.width;
			}
			return clone;
		}

		public T GetPixel(uint x, uint y) {
			return pixels[y * width + x];
		}

		public void SetPixel(uint x, uint y, T p) {
			pixels[y * width + x] = p;
		}

		public void Clear(T clearVal) {
			for (int i = 0; i < pixels.Length; i++) {
				pixels[i] = clearVal;
			}
		}

		public void GetRow(uint y, T[] row) {
			Array.Copy(pixels, (int)(y * width), row, 0, (int)width);
		}

		public void SetRow(uint y, T[] row) {
			Array.Copy(row, 0, pixels, (int)(y * width), (int)width);
		}

		public void GetRowSegment(uint y, uint x, uint pixCount, T[] segment) {
			Array.Copy(pixels, (int)(y * width + x), segment, 0, (int)pixCount);
		}

		public void SetRowSegment(uint y, uint x, uint pixCount, T[] segment) {
			Array.Copy(segment, 0, pixels, (int)(y * width + x), (int)pixCount);
		}

		public void GetRawRow(uint y, byte[] row) {
			if (typeof(T).IsPrimitive) {
				Buffer.BlockCopy(pixels, (int)(y * pitch), row, 0, (int)pitch);
			} else {
				var trow = new T[width];
				GetRow(y, trow);
				Array.Copy(RasterUtil.StructArrayToByteArray(trow), 0, row, 0, (int)pitch);
			}
		}

		public void SetRawRow(uint y, byte[] row) {
			if (typeof(T).IsPrimitive) {
				Buffer.BlockCopy(row, 0, pixels, (int)(y * pitch), (int)pitch);
			} else {
				SetRow(y, RasterUtil.ByteArrayToStructArray<T>(row));
			}
		}

		public byte[] ToByteArray() {
			if (typeof(T).IsPrimitive) {
				byte[] rasterBytes = new byte[pixels.Length * pixSize];
				Buffer.BlockCopy(pixels, 0, rasterBytes, 0, rasterBytes.Length);
				return rasterBytes;
			} else {
				return RasterUtil.StructArrayToByteArray<T>(pixels);
			}
		}

		public void YFlip() {
			T[] tempRow = new T[width];

			for (int r0 = 0, r1 = (int)((height - 1) * width); r0 < r1; r0 += (int)width, r1 -= (int)width) {
				Array.Copy(pixels, r0, tempRow, 0, (int)width);
				Array.Copy(pixels, r1, pixels, r0, (int)width);
				Array.Copy(tempRow, 0, pixels, r1, (int)width);
			}
		}

		public void XFlip() {
			for (uint r = 0, endR = width * height; r < endR; r += width) {
				for (uint dstX = 0, srcX = (width - 1), w = width / 2; dstX < w; dstX++, srcX--) {
					T a = pixels[r + srcX];
					T b = pixels[r + dstX];
					pixels[r + dstX] = a;
					pixels[r + srcX] = b;
				}
			}
		}

		public void Rotate(RasterRotation r) {
			switch (r) {
			case RasterRotation.CCW_90:
				rotate90(true);
				break;
			case RasterRotation.CW_90:
				rotate90(false);
				break;
			case RasterRotation.CCW_180:
				YFlip();
				XFlip();
				break;
			}
		}

		public T[] pixels { get; private set; }
		public uint width { get; private set; }
		public uint height { get; private set; }
		public uint pitch { get; private set; }

		private void rotate90(bool ccw) {
			uint newW = height;
			uint newH = width;
			uint newP = newW * pixSize;
			T[] newPixels = new T[width * height];

			if (ccw) {
				for (uint dstY = 0, dstR = 0, srcX = width - 1; dstY < newH; dstY++, dstR += newW, srcX--) {
					for (uint dstX = 0, srcR = 0; dstX < newW; dstX++, srcR += width) {
						newPixels[dstR + dstX] = pixels[srcR + srcX];
					}
				}
			} else {
				uint lastSrcR = (height - 1) * width;
				for (uint dstY = 0, dstR = 0, srcX = 0; dstY < newH; dstY++, dstR += newW, srcX++) {
					for (uint dstX = 0, srcR = lastSrcR; dstX < newW; dstX++, srcR -= width) {
						newPixels[dstR + dstX] = pixels[srcR + srcX];
					}
				}
			}

			width = newW;
			height = newH;
			pitch = newP;
			pixels = newPixels;
		}

		private static uint sizeofPixType() {
			return (uint)Marshal.SizeOf(typeof(T));
		}
	}

	public struct ColorF32RGB {
		public float r;
		public float g;
		public float b;
	}

	public static class RasterUtil {
		public static byte[] StructArrayToByteArray<T> (T[] source) where T : struct {
			GCHandle handle = GCHandle.Alloc(source, GCHandleType.Pinned);
			try {
				IntPtr pointer = handle.AddrOfPinnedObject();
				byte[] destination = new byte[source.Length * Marshal.SizeOf(typeof(T))];
				Marshal.Copy(pointer, destination, 0, destination.Length);
				return destination;
			} finally {
				if (handle.IsAllocated)
					handle.Free();
			}
		}

		public static T[] ByteArrayToStructArray<T>(byte[] source) where T : struct {
			T[] destination = new T[source.Length / Marshal.SizeOf(typeof(T))];
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

		public static Raster<float> Convert(this Raster<byte> src, Raster<float> dst, float transation = 0.0f, float scale = 1.0f / byte.MaxValue) {
			dst.Init(src.width, src.height);
			for (int i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = (src.pixels[i] + transation) * scale;
			}
			return dst;
		}

		public static Raster<float> Convert(this Raster<ushort> src, Raster<float> dst, float transation = 0.0f, float scale = 1.0f / ushort.MaxValue) {
			dst.Init(src.width, src.height);
			for (int i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = (src.pixels[i] + transation) * scale;
			}
			return dst;
		}

		public static Raster<ushort> Convert(this Raster<float> src, Raster<ushort> dst, float translation = 0.0f, float scale = ushort.MaxValue, float round = 0.5f) {
			dst.Init(src.width, src.height);
			for (uint i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = (ushort)((src.pixels[i] + translation) * scale + round);
			}
			return dst;
		}

		public static Raster<byte> Convert(this Raster<float> src, Raster<byte> dst, float translation = 0.0f, float scale = byte.MaxValue, float round = 0.5f) {
			dst.Init(src.width, src.height);
			for (uint i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = (byte)((src.pixels[i] + translation) * scale + round);
			}
			return dst;
		}

		public static Raster<byte> Convert(this Raster<ushort> src, Raster<byte> dst) {
			dst.Init(src.width, src.height);
			for (uint i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = (byte)(src.pixels[i] >> 8);
			}
			return dst;
		}

		public static float GetSubPixel(this Raster<float> img, float x, float y) {
			uint xlo = (uint)x;
			uint xhi = xlo + 1;
			uint rowLo = (uint)y * img.width;
			uint rowHi = rowLo + img.width;
			float fxhi = x - xlo;
			float fxlo = 1.0f - fxhi;
			float fyhi = y - (uint)y;
			float fylo = 1.0f - fyhi;

			return img.pixels[rowLo + xlo] * fylo * fxlo +
						 img.pixels[rowLo + xhi] * fylo * fxhi +
						 img.pixels[rowHi + xlo] * fyhi * fxlo +
						 img.pixels[rowHi + xhi] * fyhi * fxhi;
		}

		public static ColorF32RGB GetSubPixel(this Raster<ColorF32RGB> img, float x, float y) {
			uint xlo = (uint)x;
			uint xhi = xlo + 1;
			uint rowLo = (uint)y * img.width;
			uint rowHi = rowLo + img.width;
			float fxhi = x - xlo;
			float fxlo = 1.0f - fxhi;
			float fyhi = y - (uint)y;
			float fylo = 1.0f - fyhi;

			var aa = img.pixels[rowLo + xlo];
			var ab = img.pixels[rowLo + xhi];
			var ba = img.pixels[rowHi + xlo];
			var bb = img.pixels[rowHi + xhi];
			var aaf = fylo * fxlo;
			var abf = fylo * fxhi;
			var baf = fyhi * fxlo;
			var bbf = fyhi * fxhi;

			return new ColorF32RGB {
				r = aa.r * aaf + ab.r * abf + ba.r * baf + bb.r * bbf,
				g = aa.g * aaf + ab.g * abf + ba.g * baf + bb.g * bbf,
				b = aa.b * aaf + ab.b * abf + ba.b * baf + bb.b * bbf
			};
		}

		public static Raster<float> Scaled(this Raster<float> src, uint w, uint h) {
			if (w == src.width && h == src.height) {
				return src;
			}

			if ((w >= src.width) != (h >= src.height)) {
				if (w == src.width || h == src.height) {
					return scaledDown(src, w, h);
				}
				if (w > src.width) {
					return scaledDown(scaledUp(src, w, src.height), w, h);
				}
				return scaledDown(scaledUp(src, src.width, h), w, h);
			}

			if (w >= src.width) {
				return scaledUp(src, w, h);
			}

			return scaledDown(src, w, h);
		}

		public static Raster<ColorF32RGB> Scaled(this Raster<ColorF32RGB> src, uint w, uint h) {
			if (w == src.width && h == src.height) {
				return src;
			}

			if ((w >= src.width) != (h >= src.height)) {
				if (w == src.width || h == src.height) {
					return scaledDown(src, w, h);
				}
				if (w > src.width) {
					return scaledDown(scaledUp(src, w, src.height), w, h);
				}
				return scaledDown(scaledUp(src, src.width, h), w, h);
			}

			if (w >= src.width) {
				return scaledUp(src, w, h);
			}

			return scaledDown(src, w, h);
		}

		private static Raster<float> scaledUp(Raster<float> src, uint w, uint h) {
			while ((src.width << 1) < w && (src.height << 1) < h) {
				src = scaledUp(src, src.width << 1, src.height << 1);
			}

			while ((src.width << 1) < w) {
				src = scaledUp(src, src.width << 1, src.height);
			}

			while ((src.height << 1) < h) {
				src = scaledUp(src, src.width, src.height << 1);
			}

			var dst = new Raster<float>(w, h);

			float srcYStep = (float)(src.height - 1) / (float)(h - 1);
			float srcXStep = (float)(src.width - 1) / (float)(w - 1);
			float srcY = 0.0f;
			float srcX;

			for (uint dstY = 0; dstY < h; dstY++, srcY += srcYStep) {
				srcX = 0.0f;
				for (uint dstX = 0; dstX < w; dstX++, srcX += srcXStep) {
					var p = src.GetSubPixel(srcX, srcY);
					dst.SetPixel(dstX, dstY, p);
				}
			}

			return dst;
		}

		private static Raster<float> scaledDown(Raster<float> src, uint w, uint h) {
			while ((src.width >> 1) > w && (src.height >> 1) > h) {
				src = scaledDown(src, src.width >> 1, src.height >> 1);
			}

			while ((src.width >> 1) > w) {
				src = scaledDown(src, src.width >> 1, src.height);
			}

			while ((src.height >> 1) > h) {
				src = scaledDown(src, src.width, src.height >> 1);
			}

			var dst = new Raster<float>(w, h);

			float srcYStep = (float)src.height / (float)h;
			float srcXStep = (float)src.width / (float)w;
			float srcY = (srcYStep - 1) * 0.5f;
			float srcX;

			for (uint dstY = 0; dstY < h; dstY++, srcY += srcYStep) {
				srcX = (srcXStep - 1) * 0.5f;
				for (uint dstX = 0; dstX < w; dstX++, srcX += srcXStep) {
					var p = src.GetSubPixel(srcX, srcY);
					dst.SetPixel(dstX, dstY, p);
				}
			}

			return dst;
		}

		private static Raster<ColorF32RGB> scaledUp(Raster<ColorF32RGB> src, uint w, uint h) {
			while ((src.width << 1) < w && (src.height << 1) < h) {
				src = scaledUp(src, src.width << 1, src.height << 1);
			}

			while ((src.width << 1) < w) {
				src = scaledUp(src, src.width << 1, src.height);
			}

			while ((src.height << 1) < h) {
				src = scaledUp(src, src.width, src.height << 1);
			}

			var dst = new Raster<ColorF32RGB>(w, h);

			float srcYStep = (float)(src.height - 1) / (float)(h - 1);
			float srcXStep = (float)(src.width - 1) / (float)(w - 1);
			float srcY = 0.0f;
			float srcX;

			for (uint dstY = 0; dstY < h; dstY++, srcY += srcYStep) {
				srcX = 0.0f;
				for (uint dstX = 0; dstX < w; dstX++, srcX += srcXStep) {
					var p = src.GetSubPixel(srcX, srcY);
					dst.SetPixel(dstX, dstY, p);
				}
			}

			return dst;
		}

		private static Raster<ColorF32RGB> scaledDown(Raster<ColorF32RGB> src, uint w, uint h) {
			while ((src.width >> 1) > w && (src.height >> 1) > h) {
				src = scaledDown(src, src.width >> 1, src.height >> 1);
			}

			while ((src.width >> 1) > w) {
				src = scaledDown(src, src.width >> 1, src.height);
			}

			while ((src.height >> 1) > h) {
				src = scaledDown(src, src.width, src.height >> 1);
			}

			var dst = new Raster<ColorF32RGB>(w, h);

			float srcYStep = (float)src.height / (float)h;
			float srcXStep = (float)src.width / (float)w;
			float srcY = (srcYStep - 1) * 0.5f;
			float srcX;

			for (uint dstY = 0; dstY < h; dstY++, srcY += srcYStep) {
				srcX = (srcXStep - 1) * 0.5f;
				for (uint dstX = 0; dstX < w; dstX++, srcX += srcXStep) {
					var p = src.GetSubPixel(srcX, srcY);
					dst.SetPixel(dstX, dstY, p);
				}
			}

			return dst;
		}
	}
}
