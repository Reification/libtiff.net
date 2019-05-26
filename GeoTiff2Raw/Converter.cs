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
				log("Exception caught:\n{0}", ex.Message);
			}
			return false;
		}

		static void log(string message, params Object[] args) {
			Console.WriteLine(string.Format(message, args));
		}

		static void warn(string message, params Object[] args) {
			Console.WriteLine(string.Format(message, args));
		}

		static void error(string message, params Object[] args) {
			throw new Exception(string.Format(message, args));
		}

		struct dv3 {
			public double x;
			public double y;
			public double z;
		}

		struct TiePoint {
			public dv3 rasterPt;
			public dv3 modelPt;
		}

		enum GeoTiffTag {
			MODELPIXELSCALETAG = 33550,
			MODELTIEPOINTTAG = 33922,
			MODELTRANSFORMATIONTAG = 34264,
			GEOKEYDIRECTORYTAG = 34735,
			GEODOUBLEPARAMSTAG = 34736,
			GEOASCIIPARAMSTAG = 34737,
			GDALNODATATAG = 42113
		}

		class ExtendedTagMethods : TiffTagMethods {
			public ExtendedTagMethods() {
			}

			public override FieldValue[] GetField(Tiff tif, TiffTag tag) {
				GeoTiffTag gtag = (GeoTiffTag)(int)tag;
				FieldValue[] rawResult = base.GetField(tif, (TiffTag)(int)gtag);
				FieldValue[] result = null;

				switch ((int)tag) {
				case (int)GeoTiffTag.MODELPIXELSCALETAG:
					result = rawResult;
					break;
				case (int)GeoTiffTag.MODELTIEPOINTTAG:
					result = rawResult;
					break;
				case (int)GeoTiffTag.MODELTRANSFORMATIONTAG:
					result = rawResult;
					break;
				case (int)GeoTiffTag.GEOKEYDIRECTORYTAG:
					result = rawResult;
					break;
				case (int)GeoTiffTag.GEODOUBLEPARAMSTAG:
					result = rawResult;
					break;
				case (int)GeoTiffTag.GEOASCIIPARAMSTAG:
					result = rawResult;
					break;
				case (int)GeoTiffTag.GDALNODATATAG:
					result = rawResult;
					break;
				default:
					result = rawResult;
					break;
				}

				return result;
			}
		}

		static dv3 getModelPixelScale(Tiff tif) {
			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.MODELPIXELSCALETAG);
			if (v?.Length == 2 && v[0].ToInt() == 3) {
				double[] arr = v[1].ToDoubleArray();
				var val = new dv3 { x = arr[0], y = arr[1], z = arr[2] };
				return val;
			}
			return new dv3 { x = 0, y = 0, z = 0 };
		}
		static TiePoint[] getModelTiePoints(Tiff tif) {
			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.MODELTIEPOINTTAG);
			if (v?.Length == 2 && v[0].ToInt() > 0 && (v[0].ToInt() % 6) == 0) {
				double[] arr = v[1].ToDoubleArray();
				TiePoint[] pts = new TiePoint[arr.Length / 6];
				for ( int i = 0, j = 0; i < pts.Length; i+=1, j+=6 ) {
					pts[i].rasterPt = new dv3 { x = arr[j + 0], y = arr[j + 1], z = arr[j + 2] };
					pts[i].modelPt = new dv3 { x = arr[j + 3], y = arr[j + 4], z = arr[j + 5] };
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

		static short[] getGeoKeyDirectory(Tiff tif) {
			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.GEOKEYDIRECTORYTAG);
			if (v?.Length == 2 && v[0].ToInt() >= 4) {
				var val = v[1].ToShortArray();
				var strVal = v[1].ToString();
				return val;
			}
			return null;
		}

		static double[] getGeoDoublesParams(Tiff tif) {
			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.GEODOUBLEPARAMSTAG);
			if (v?.Length == 2 && v[0].ToInt() > 0) {
				var val = v[1].ToDoubleArray();
				return val;
			}
			return null;
		}

		static string getGeoAsciiParams(Tiff tif) {
			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.GEOASCIIPARAMSTAG);
			if (v?.Length >= 2) {
				string ascii = v[1].ToString();
				return ascii;
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

		void extenderProc(Tiff t) {
			t.SetTagMethods(new ExtendedTagMethods());
		}

		void go() {
			// just useful for examining extended values. not necessary.
			Tiff.SetTagExtender((Tiff t) => { this.extenderProc(t); });

			using (Tiff inImage = Tiff.Open(inputTiffPath, "r")) {
				using (var stdout = Console.OpenStandardOutput()) {
					inImage.PrintDirectory(stdout, TiffPrintFlags.NONE);
				}

				int width = inImage.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
				int height = inImage.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
				int rowsPerStrip = inImage.GetField(TiffTag.ROWSPERSTRIP)[0].ToInt();

				bool isByteSwapped = inImage.IsByteSwapped();

				if ( rowsPerStrip != 1 ) {
					error("Images must be stripped with 1 row per strip. {0} rows per strip not supported.", rowsPerStrip);
				}

				dv3 scale = getModelPixelScale(inImage);
				TiePoint[] tiePoints = getModelTiePoints(inImage);
				short[] geoKeys = getGeoKeyDirectory(inImage);
				double[] doubleParams = getGeoDoublesParams(inImage);
				string asciiParams = getGeoAsciiParams(inImage);
				int noDataValue = getGdalNoData(inImage);

				float minVal = (float)inImage.GetField(TiffTag.SMINSAMPLEVALUE)[0].ToDouble();
				float maxVal = (float)inImage.GetField(TiffTag.SMAXSAMPLEVALUE)[0].ToDouble();
				float maxScale = (float)(ushort.MaxValue / inImage.GetField(TiffTag.SMAXSAMPLEVALUE)[0].ToDouble());

				var rawRaster = new ushort[width * height];
				var srcRow = new float[width];
				var srcByteRow = new byte[width * 4];

				var dstRowIdx = 0;

				for ( int y = 0; y < height; y++, dstRowIdx += width ) {
					int readCount = inImage.ReadEncodedStrip(y, srcByteRow, 0, -1);
					if ( readCount != width * 4 ) {
						error("invalid strip size bytes. Expected {0} got {1}", width * 4, readCount);
					}

					if ( isByteSwapped ) {
						for ( int b = 0, bc = width * 4; b < bc; b += 4 ) {
							byte t = srcByteRow[b + 0];
							srcByteRow[b + 0] = srcByteRow[b + 3];
							srcByteRow[b + 3] = t;
							t = srcByteRow[b + 1];
							srcByteRow[b + 1] = srcByteRow[b + 2];
							srcByteRow[b + 2] = t;
						}
					}

					Buffer.BlockCopy(srcByteRow, 0, srcRow, 0, srcByteRow.Length);

					for (int x = 0; x < width; x++) {
						rawRaster[dstRowIdx + x] = (ushort)((srcRow[x] - minVal) * maxScale);
					}
				}

				using (var outFile = new FileStream(outputRawPath, FileMode.Create, FileAccess.Write)) {
					var rasterBytes = new byte[rawRaster.Length * 2];
					Buffer.BlockCopy(rawRaster, 0, rasterBytes, 0, rawRaster.Length);
					outFile.Write(rasterBytes, 0, rasterBytes.Length);
				}
			}
		}
	}
}
