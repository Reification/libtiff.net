using System;
using System.IO;
using BitMiracle.LibTiff.Classic;

namespace GeoTiff2Unity {
	public class Converter {

		public string inputFloatHeightTifPath = null;
		public string inputRGBTifPath = null;
		public string outputRawHeightPath = null;
		public string outputRGBTifPath = null;

		public bool Go() {
			try {
				go();
				return true;
			} catch (Exception ex) {
				Util.Log("Exception caught:\n{0}", ex.Message);
			}
			return false;
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
			using (Tiff inImage = Tiff.Open(inputFloatHeightTifPath, "r")) {
				using (var stdout = Console.OpenStandardOutput()) {
					inImage.PrintDirectory(stdout, TiffPrintFlags.NONE);
				}

				int width = inImage.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
				int height = inImage.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
				int rowsPerStrip = inImage.GetField(TiffTag.ROWSPERSTRIP)[0].ToInt();

				if ( width <= 0 || height <= 0 ) {
					Util.Error("Invalid image size {0}x{1}", width, height);
				}

				if (rowsPerStrip != 1) {
					Util.Error("Invalid image rows per strip {0}. Only 1 row per strip supported.", rowsPerStrip);
				}

				VectorD3 scale = GeoKeyDir.GetModelPixelScale(inImage);
				TiePoint[] tiePoints = GeoKeyDir.GetModelTiePoints(inImage);
				GeoKeyDir geoKeys = GeoKeyDir.GetGeoKeyDir(inImage);

				float minVal = (float)inImage.GetField(TiffTag.SMINSAMPLEVALUE)[0].ToDouble();
				float maxVal = (float)inImage.GetField(TiffTag.SMAXSAMPLEVALUE)[0].ToDouble();

				var rasterF32 = new Raster<float>((uint)width, (uint)height);
				var srcByteRow = new byte[width * sizeof(float)];

				bool isByteSwapped = inImage.IsByteSwapped();

				for (int y = 0, dstRowIdx = 0; y < height; y++, dstRowIdx += width) {
					int readCount = inImage.ReadEncodedStrip(y, srcByteRow, 0, -1);

					if (readCount != srcByteRow.Length) {
						Util.Error("invalid strip size bytes. Expected {0} got {1}", srcByteRow.Length, readCount);
					}

					if (isByteSwapped) {
						Util.ByteSwap4(srcByteRow);
					}

					rasterF32.SetRawRow((uint)y, srcByteRow);
				}

				float noDataValue = (float)getGdalNoData(inImage);

				{
					int noDataCount = 0;
					for (int i = 0; i < rasterF32.pixels.Length; i++) {
						if (rasterF32.pixels[i] == noDataValue) {
							rasterF32.pixels[i] = (float)minVal;
							noDataCount++;
						}
					}
					if ( noDataCount > 0 ) {
						Util.Warn("Replaced {0} no-data pixels with minValue {1}", noDataCount, minVal);
					}
				}

				uint outWidth = (uint)Math.Pow(2.0, Math.Floor(Math.Log(width - 1) / Math.Log(2.0))) + 1;
				uint outHeight = (uint)Math.Pow(2.0, Math.Floor(Math.Log(height - 1) / Math.Log(2.0))) + 1;

				// let's just make it square for now.
				outWidth = outHeight = Math.Min(outWidth, outHeight);

				// crop to bottom right (for now)
				rasterF32 = rasterF32.Clone((uint)width - outWidth, (uint)height - outHeight, outWidth, outHeight);

				minVal = float.MaxValue;
				maxVal = float.MinValue;
				for (int i = 0; i < rasterF32.pixels.Length; i++) {
					var p = rasterF32.pixels[i];
					minVal = Math.Min(minVal, p);
					maxVal = Math.Max(maxVal, p);
				}

				var rawHeightMap = new Raster<ushort>();

				// do this if we want to map used range across the entire value range of the target type.
				// requires that we pay attention to the max value comment in the output for plugging in
				// to unity when importing raw heightmap.
				float toRawTranslation = (float)-minVal;
				float toRawScale = (float)((Math.Pow(2.0, rawHeightMap.bitsPerPixel) - 1) / (maxVal + toRawTranslation));

				rasterF32.Convert(rawHeightMap, toRawTranslation, toRawScale);
				rasterF32 = null;

				// Unity's default heightmap orientation is upside-down WRT RGB textures.
				rawHeightMap.YFlip();

				using (var outFile = new FileStream(outputRawHeightPath, FileMode.Create, FileAccess.Write)) {
					var rasterBytes = rawHeightMap.ToByteArray();
					outFile.Write(rasterBytes, 0, rasterBytes.Length);
				}

				Util.Log("Ouput Raw Height Map: {0}", outputRawHeightPath);
				Util.Log("  Size: {0}x{1} pix", rawHeightMap.width, rawHeightMap.height);

				Util.Log("Wrote {0}x{1} {2} [{3} {4}] {5}bpp raw image with max sample value {6} to {7}", 
					rawHeightMap.width, 
					rawHeightMap.height, 
					geoKeys.projLinearUnit,
					(maxVal + toRawTranslation),
					geoKeys.verticalLinearUnit,
					rawHeightMap.bitsPerPixel,
					(uint)((maxVal + toRawTranslation)*toRawScale + 0.5f),
					outputRawHeightPath);
			}
		}
	}
}
