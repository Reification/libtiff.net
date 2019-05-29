using System;
using System.IO;
using BitMiracle.LibTiff.Classic;

namespace GeoTiff2Raw {
	public class Converter {
		public string inputTiffPath = null;
		public string outputRawPath = null;

		public bool Go() {
			try {
				go();
				return true;
			} catch (Exception ex) {
				Util.Log("Exception caught:\n{0}", ex.Message);
			}
			return false;
		}

		static VectorD3 getModelPixelScale(Tiff tif) {
			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.MODELPIXELSCALETAG);
			if (v?.Length == 2 && v[0].ToInt() == 3) {
				double[] arr = v[1].ToDoubleArray();
				var val = new VectorD3 { x = arr[0], y = arr[1], z = arr[2] };
				return val;
			}
			return new VectorD3 { x = 0, y = 0, z = 0 };
		}

		static TiePoint[] getModelTiePoints(Tiff tif) {
			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.MODELTIEPOINTTAG);
			if (v?.Length == 2 && v[0].ToInt() > 0 && (v[0].ToInt() % 6) == 0) {
				double[] arr = v[1].ToDoubleArray();
				TiePoint[] pts = new TiePoint[arr.Length / 6];
				for (int i = 0, j = 0; i < pts.Length; i += 1, j += 6) {
					pts[i].rasterPt = new VectorD3 { x = arr[j + 0], y = arr[j + 1], z = arr[j + 2] };
					pts[i].modelPt = new VectorD3 { x = arr[j + 3], y = arr[j + 4], z = arr[j + 5] };
				}
				return pts;
			}
			return null;
		}

		static double[] getModelTransformation(Tiff tif) {
			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.MODELTRANSFORMATIONTAG);
			if (v?.Length == 2 && v[0].ToInt() == 16) {
				var val = v[1].ToDoubleArray();
				return val;
			}
			return null;
		}

		static int getGdalNoData(Tiff tif) {
			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.GDALNODATATAG);
			if (v?.Length > 1) {
				int val = int.Parse(v[1].ToString());
				return val;
			}
			return int.MinValue;
		}

		void go() {
			using (Tiff inImage = Tiff.Open(inputTiffPath, "r")) {
				using (var stdout = Console.OpenStandardOutput()) {
					inImage.PrintDirectory(stdout, TiffPrintFlags.NONE);
				}

				int width = inImage.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
				int height = inImage.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
				int rowsPerStrip = inImage.GetField(TiffTag.ROWSPERSTRIP)[0].ToInt();

				bool isByteSwapped = inImage.IsByteSwapped();

				if (rowsPerStrip != 1) {
					Util.Error("Images must be stripped with 1 row per strip. {0} rows per strip not supported.", rowsPerStrip);
				}

				VectorD3 scale = getModelPixelScale(inImage);
				TiePoint[] tiePoints = getModelTiePoints(inImage);
				GeoKeyDir geoKeys = GeoKeyDir.GetGeoKeyDir(inImage);
				float noDataValue = (float)getGdalNoData(inImage);

				double minVal = inImage.GetField(TiffTag.SMINSAMPLEVALUE)[0].ToDouble();
				double maxVal = inImage.GetField(TiffTag.SMAXSAMPLEVALUE)[0].ToDouble();
				double maxScale = (ushort.MaxValue / (maxVal - minVal));

				RasterGrayF32 rasterF32 = new RasterGrayF32((uint)width, (uint)height);

				var srcByteRow = new byte[width * sizeof(float)];

				for (int y = 0, dstRowIdx = 0; y < height; y++, dstRowIdx += width) {
					int readCount = inImage.ReadEncodedStrip(y, srcByteRow, 0, -1);

					if (readCount != srcByteRow.Length) {
						Util.Error("invalid strip size bytes. Expected {0} got {1}", srcByteRow.Length, readCount);
					}

					if (isByteSwapped) {
						Util.ByteSwap4(srcByteRow);
					}

					rasterF32.SetRow((uint)y, srcByteRow);
				}

				for (int i = 0; i < rasterF32.pixels.Length; i++) {
					if ( rasterF32.pixels[i] == noDataValue ) {
						rasterF32.pixels[i] = 0.0f;
					}
				}

				double outWidth = Math.Pow(2.0, Math.Floor(Math.Log(width) / Math.Log(2.0))) + 1.0;
				double outHeight = Math.Floor(height * (outWidth / width) + 0.5);

				rasterF32 = rasterF32.Scaled((uint)outWidth, (uint)outHeight);

				outHeight = Math.Min(outWidth, outHeight);

				//RasterGrayU16 rasterU16 = new RasterGrayU16(rasterF32, minVal, maxScale);
				RasterGrayU16 rasterU16 = new RasterGrayU16(rasterF32.Clone((uint)(rasterF32.width - outWidth), (uint)(rasterF32.height - outHeight), (uint)outWidth, (uint)outHeight), minVal, maxScale);

				rasterF32.Init(0, 0);
				rasterF32 = null;

				using (var outFile = new FileStream(outputRawPath, FileMode.Create, FileAccess.Write)) {
					var rasterBytes = rasterU16.ToByteArray();
					outFile.Write(rasterBytes, 0, rasterBytes.Length);
				}

				Util.Log("Wrote {0}x{1} u16 raw image to {2}", rasterU16.width, rasterU16.height, outputRawPath);
			}
		}
	}
}
