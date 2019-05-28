using System;
using System.IO;
using System.Collections.Generic;
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
				for (int i = 0, j = 0; i < pts.Length; i += 1, j += 6) {
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

		struct GeoKeyDirRaw {
			public ushort keyDirVersion;
			public ushort keyMajorRevision;
			public ushort keyMinorRevision;
			public ushort keyCount;
		}


		struct GeoKeyRaw {
			public ushort keyId;
			public ushort tiffTagLocation;
			public ushort valueCount;
			public ushort valueOffset;
		}

		static bool checkEnumVal<E>(E v) {
			if (!Enum.IsDefined(typeof(E), v)) {
				warn("Assigned to {0} unknown value {1}", typeof(E), v);
				return false;
			}
			return true;
		}

		class GeoKeyDir {
			public GeoKeyDir() {
				rawHeader.keyDirVersion = 0;
				rawHeader.keyMajorRevision = 0;
				rawHeader.keyMajorRevision = 0;
				rawHeader.keyCount = 0;
			}

			public ModelType modelType = ModelType.Undefined;
			public RasterType rasterType = RasterType.Undefined;
			public GeographicCSType geographicCSType = GeographicCSType.Undefined;
			public GeodeticDatumCode geodeticDatumCode = GeodeticDatumCode.Undefined;
			public PrimeMeridianCode geogPrimeMeridianCode = PrimeMeridianCode.Undefined;
			public LinearUnitCode geogLinearUnit = LinearUnitCode.Undefined;
			public AngularUnitCode geogAngularUnit = AngularUnitCode.Undefined;
			public EllipsoidCode geogEllipsoid = EllipsoidCode.Undefined;
			public AngularUnitCode geogAzimuthUnit = AngularUnitCode.Undefined;

			public ProjectedCSTypeCode projectedCSType = ProjectedCSTypeCode.Undefined;
			public ProjectionCode projectionCode = ProjectionCode.Undefined;
			public CoordTransformCode coordTransformCode = CoordTransformCode.Undefined;
			public LinearUnitCode projLinearUnit = LinearUnitCode.Undefined;

			public VerticalCSTypeCode verticalCSType = VerticalCSTypeCode.Undefined;
			public LinearUnitCode verticalLinearUnit = LinearUnitCode.Undefined;

			public string gtCitation = "";
			public string geogCitation = "";
			public string pcsCitation = "";
			public string verticalCitation = "";

			public double geogPrimeMeridianLon = -1.0;
			public double geogLinearUnitSize = 0.0;
			public double geogAngularUnitsSize = 0.0;
			public double geogSemiMajorAxis = -1.0;
			public double geogSemiMinorAxis = -1.0;
			public double geogInvFlattening = -1.0;
			public double projLinearUnitSize = 0.0;
			public double projStdParallel1 = -1.0;
			public double projStdParallel2 = -1.0;
			public double projNatOriginLon = -1.0;
			public double projNatOriginLat = -1.0;
			public double projFalseEasting = 0.0;
			public double projFalseNorthing = 0.0;
			public double projFalseOriginLat = 0.0;
			public double projFalseOriginLon = 0.0;
			public double projFalseOriginEasting = 0.0;
			public double projFalseOriginNorthing = 0.0;
			public double projCenterLon = 0.0;
			public double projCenterLat = 0.0;
			public double projCenterEasting = 0.0;
			public double projCenterNorthing = 0.0;
			public double projScaleAtNatOrigin = 0.0;
			public double projScaleAtCenter = 0.0;
			public double projAzimuthAngle = 0.0;
			public double projStraightVertPoleLon = 0.0;

			public bool addCodeValue(GeoKey key, ushort value) {
				codeValues.Add(key, value);
				switch (key) {
				case GeoKey.GTModelType:
					modelType = (ModelType)value;
					checkEnumVal(modelType);
					break;
				case GeoKey.GTRasterType:
					rasterType = (RasterType)value;
					checkEnumVal(rasterType);
					break;
				case GeoKey.GeogType:
					geographicCSType = (GeographicCSType)value;
					checkEnumVal(geographicCSType);
					break;
				case GeoKey.GeogGeodeticDatum:
					geodeticDatumCode = (GeodeticDatumCode)value;
					checkEnumVal(geodeticDatumCode);
					break;
				case GeoKey.GeogPrimeMeridian:
					geogPrimeMeridianCode = (PrimeMeridianCode)value;
					checkEnumVal(geogPrimeMeridianCode);
					break;
				case GeoKey.GeogLinearUnits:
					geogLinearUnit = (LinearUnitCode)value;
					checkEnumVal(geogLinearUnit);
					break;
				case GeoKey.GeogAngularUnits:
					geogAngularUnit = (AngularUnitCode)value;
					checkEnumVal(geogAngularUnit);
					break;
				case GeoKey.GeogEllipsoid:
					geogEllipsoid = (EllipsoidCode)value;
					checkEnumVal(geogEllipsoid);
					break;
				case GeoKey.GeogAzimuthUnits:
					geogAzimuthUnit = (AngularUnitCode)value;
					checkEnumVal(geogAzimuthUnit);
					break;
				case GeoKey.ProjectedCSType:
					projectedCSType = (ProjectedCSTypeCode)value;
					checkEnumVal(projectedCSType);
					break;
				case GeoKey.Projection:
					projectionCode = (ProjectionCode)value;
					checkEnumVal(projectionCode);
					break;
				case GeoKey.ProjCoordTrans:
					coordTransformCode = (CoordTransformCode)value;
					checkEnumVal(coordTransformCode);
					break;
				case GeoKey.VerticalCSType:
					verticalCSType = (VerticalCSTypeCode)value;
					checkEnumVal(verticalCSType);
					break;
				case GeoKey.VerticalUnits:
					verticalLinearUnit = (LinearUnitCode)value;
					checkEnumVal(verticalLinearUnit);
					break;
				case GeoKey.ProjLinearUnits:
					projLinearUnit = (LinearUnitCode)value;
					checkEnumVal(projLinearUnit);
					break;
				default:
					warn("Assigning to unknown key {0} code value {1}", (int)key, value);
					return false;
				}
				return true;
			}

			public bool addAsciiValue(GeoKey key, string value) {
				asciiValues.Add(key, value);
				switch ( key ) {
				case GeoKey.GTCitation:
					gtCitation = value;
					break;
				case GeoKey.GeogCitation:
					geogCitation = value;
					break;
				case GeoKey.PCSCitation:
					pcsCitation = value;
					break;
				case GeoKey.VerticalCitation:
					verticalCitation = value;
					break;
				default:
					warn("Assigning to unknown key {0} string value {1}", (int)key, value);
					return false;
				}
				return true;
			}

			public bool addDoubleValue(GeoKey key, double value) {
				doubleValues.Add(key, value);

				switch ( key ) {
				case GeoKey.GeogPrimeMeridianLon:
					geogPrimeMeridianLon = value;
					break;
				case GeoKey.GeogLinearUnitSize:
					geogLinearUnitSize = value;
					break;
				case GeoKey.GeogAngularUnitSize:
					geogAngularUnitsSize = value;
					break;
				case GeoKey.GeogSemiMajorAxis:
					geogSemiMajorAxis = value;
					break;
				case GeoKey.GeogSemiMinorAxis:
					geogSemiMinorAxis = value;
					break;
				case GeoKey.GeogInvFlattening:
					geogInvFlattening = value;
					break;
				case GeoKey.ProjLinearUnitSize:
					projLinearUnitSize = value;
					break;
				case GeoKey.ProjStdParallel1:
					projStdParallel1 = value;
					break;
				case GeoKey.ProjStdParallel2:
					projStdParallel2 = value;
					break;
				case GeoKey.ProjNatOriginLat:
					projNatOriginLat = value;
					break;
				case GeoKey.ProjNatOriginLon:
					projNatOriginLon = value;
					break;
				case GeoKey.ProjFalseEasting:
					projFalseEasting = value;
					break;
				case GeoKey.ProjFalseNorthing:
					projFalseNorthing = value;
					break;
				case GeoKey.ProjFalseOriginLat:
					projFalseOriginLat = value;
					break;
				case GeoKey.ProjFalseOriginLon:
					projFalseOriginLon = value;
					break;
				case GeoKey.ProjFalseOriginEasting:
					projFalseOriginEasting = value;
					break;
				case GeoKey.ProjFalseOriginNorthing:
					projFalseOriginNorthing = value;
					break;
				case GeoKey.ProjCenterLat:
					projCenterLat = value;
					break;
				case GeoKey.ProjCenterLon:
					projCenterLon = value;
					break;
				case GeoKey.ProjCenterEasting:
					projCenterEasting = value;
					break;
				case GeoKey.ProjCenterNorthing:
					projCenterNorthing = value;
					break;
				case GeoKey.ProjScaleAtNatOrigin:
					projScaleAtNatOrigin = value;
					break;
				case GeoKey.ProjScaleAtCenter:
					projScaleAtCenter = value;
					break;
				case GeoKey.ProjAzimuthAngle:
					projAzimuthAngle = value;
					break;
				case GeoKey.ProjStraightVertPoleLon:
					projStraightVertPoleLon = value;
					break;
				default:
					warn("Assigning to unknown key {0} double value {1}", (int)key, value);
					return false;
				}

				return true;
			}

			public GeoKeyDirRaw rawHeader = new GeoKeyDirRaw();
			public GeoKeyRaw[] rawKeys = new GeoKeyRaw[0];
			public double[] doubleParams = new double[0];
			public string[] asciiParams = new string[0];

			public Dictionary<GeoKey, ushort> codeValues = new Dictionary<GeoKey, ushort>();
			public Dictionary<GeoKey, string> asciiValues = new Dictionary<GeoKey, string>();
			public Dictionary<GeoKey, double> doubleValues = new Dictionary<GeoKey, double>();
		}

		static double[] getGeoDoublesParams(Tiff tif) {
			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.GEODOUBLEPARAMSTAG);
			if (v?.Length == 2 && v[0].ToInt() > 0) {
				var val = v[1].ToDoubleArray();
				return val;
			}
			return new double[0];
		}

		static string[] getGeoAsciiParams(Tiff tif) {
			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.GEOASCIIPARAMSTAG);
			if (v?.Length >= 2) {
				string ascii = v[1].ToString();
				return ascii.Split('|');
			}
			return new string[0];
		}

		static GeoKeyDir getGeoKeyDirectory(Tiff tif) {
			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.GEOKEYDIRECTORYTAG);
			if (v?.Length == 2 && v[0].ToInt() >= 4) {
				double[] doubleParams = getGeoDoublesParams(tif);
				string[] asciiParams = getGeoAsciiParams(tif);
				var val = v[1].ToUShortArray();
				GeoKeyDir keyDir = new GeoKeyDir();

				keyDir.rawHeader.keyDirVersion = val[0];

				if (keyDir.rawHeader.keyDirVersion != 1) {
					error("GeoKeyDir version must be 1, was {0}", keyDir.rawHeader.keyDirVersion);
				}

				keyDir.rawHeader.keyMajorRevision = val[1];
				keyDir.rawHeader.keyMinorRevision = val[2];
				keyDir.rawHeader.keyCount = val[3];

				keyDir.doubleParams = doubleParams;
				keyDir.asciiParams = asciiParams;

				keyDir.rawKeys = new GeoKeyRaw[keyDir.rawHeader.keyCount];

				for (int ki = 0, di = 4; ki < keyDir.rawKeys.Length; ki++, di += 4) {
					if (di >= val.Length) {
						error("GeoKey parsing of key {0}/{1} ran off the end of the incoming data array",
							ki, keyDir.rawKeys.Length);
					}

					GeoKey key = 0;
					GeoTiffTag valueSourceTag = 0;
					ushort valCount = 0;
					ushort valOffset = 0;

					key = (GeoKey)(keyDir.rawKeys[ki].keyId = val[di + 0]);
					valueSourceTag = (GeoTiffTag)(keyDir.rawKeys[ki].tiffTagLocation = val[di + 1]);
					valCount = keyDir.rawKeys[ki].valueCount = val[di + 2];
					valOffset = keyDir.rawKeys[ki].valueOffset = val[di + 3];

					checkEnumVal(key);

					switch (valueSourceTag) {
					case GeoTiffTag.NONE:
						if (valCount != 1) {
							error("GeoKey[{0}] {1} (ushort) valCount is {2}, only 1 value per key supported.", ki, key, valCount);
						}
						keyDir.addCodeValue(key, valOffset);
						break;
					case GeoTiffTag.GEODOUBLEPARAMSTAG:
						if (valCount != 1) {
							error("GeoKey[{0}] {1} (double) valCount is {2}, only 1 value per key supported.", ki, key, valCount);
						}
						keyDir.addDoubleValue(key, doubleParams[valOffset]);
						break;
					case GeoTiffTag.GEOASCIIPARAMSTAG:
						if (valCount < 1) {
							error("GeoKey[{0}] {1} (ascii) valCount is {2}, must be >= 1.", ki, key, valCount);
						}
						string asciiVal = "";
						// value offset is index into string buffer with all values separated by |
						// we convert to array of strings so need to convert from buffer inset to string array index.
						for (int strIdx = 0, bufInset = valOffset; bufInset >= 0;) {
							asciiVal = asciiParams[strIdx++];
							bufInset -= asciiVal.Length;
						}
						if ( valCount != asciiVal.Length + 1 ) {
							error( "GeoKey[{0}] {1} (ascii) has corrupt string reference. Expected {2} chars, got {3}", ki, key, valCount, asciiVal.Length + 1 );
						}
						keyDir.addAsciiValue(key, asciiVal);
						break;
					default:
						error("GeoKey[{0}] {1} tiffTaglLocation {2} is invalid. Must be 0, GEODOUBLEPARAMSTAG or GEOASCIIPARAMSTAG", ki, key, valueSourceTag);
						break;
					}
				}

				return keyDir;
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

		static void byteSwap2(byte[] arr) {
			if ((arr.Length & 0x01) != 0) {
				error("byteSwap2 array length {0} not a multiple of 2!", arr.Length);
			}
			for (int b = 0; b < arr.Length; b += 2) {
				byte t = arr[b + 0];
				arr[b + 0] = arr[b + 1];
				arr[b + 1] = t;
			}
		}

		static void byteSwap4(byte[] arr) {
			if ((arr.Length & 0x03) != 0) {
				error("byteSwap4 array length {0} not a multiple of 4!", arr.Length);
			}
			for (int b = 0; b < arr.Length; b += 4) {
				byte t = arr[b + 0];
				arr[b + 0] = arr[b + 3];
				arr[b + 3] = t;
				t = arr[b + 1];
				arr[b + 1] = arr[b + 2];
				arr[b + 2] = t;
			}
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
					error("Images must be stripped with 1 row per strip. {0} rows per strip not supported.", rowsPerStrip);
				}

				dv3 scale = getModelPixelScale(inImage);
				TiePoint[] tiePoints = getModelTiePoints(inImage);
				GeoKeyDir geoKeys = getGeoKeyDirectory(inImage);
				int noDataValue = getGdalNoData(inImage);

				double minVal = inImage.GetField(TiffTag.SMINSAMPLEVALUE)[0].ToDouble();
				double maxVal = inImage.GetField(TiffTag.SMAXSAMPLEVALUE)[0].ToDouble();
				double maxScale = (ushort.MaxValue / (maxVal - minVal));

				int pixCount = width * height;
				int rowByteCount = width * 4;
				var rawRaster = new ushort[pixCount];
				var srcRow = new float[width];
				var srcByteRow = new byte[rowByteCount];

				for (int y = 0, dstRowIdx = 0; y < height; y++, dstRowIdx += width) {
					int readCount = inImage.ReadEncodedStrip(y, srcByteRow, 0, -1);

					if (readCount != rowByteCount) {
						error("invalid strip size bytes. Expected {0} got {1}", rowByteCount, readCount);
					}

					if (isByteSwapped) {
						byteSwap4(srcByteRow);
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
