using System;
using System.Collections.Generic;

using BitMiracle.LibTiff.Classic;

using R9N;

namespace GeoTiff2Unity {
	public struct TiePoint {
		public VectorD3 rasterPt;
		public VectorD3 modelPt;

		public override string ToString() {
			return string.Format("rasterPt: {0} modelPt: {1}", rasterPt, modelPt);
		}
	}

	public struct GeoKeyDirRaw {
		public ushort keyDirVersion;
		public ushort keyMajorRevision;
		public ushort keyMinorRevision;
		public ushort keyCount;
	}

	public struct GeoKeyRaw {
		public ushort keyId;
		public ushort tiffTagLocation;
		public ushort valueCount;
		public ushort valueOffset;
	}

	public class GeoKeyDir {
		public static GeoKeyDir GetGeoKeyDir(Tiff tif) {
			Util.Log("Parsing GeoKeyDir");

			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.GEOKEYDIRECTORYTAG);
			if (v?.Length == 2 && v[0].ToInt() >= 4) {
				double[] doubleParams = getGeoDoublesParams(tif);
				string asciiParams = getGeoAsciiParams(tif);
				var val = v[1].ToUShortArray();
				GeoKeyDir keyDir = new GeoKeyDir();

				keyDir.rawHeader.keyDirVersion = val[0];

				if (keyDir.rawHeader.keyDirVersion != 1) {
					Util.Error("  GeoKeyDir version must be 1, was {0}", keyDir.rawHeader.keyDirVersion);
				}

				keyDir.rawHeader.keyMajorRevision = val[1];
				keyDir.rawHeader.keyMinorRevision = val[2];
				keyDir.rawHeader.keyCount = val[3];

				keyDir.doubleParams = doubleParams;
				keyDir.asciiParams = asciiParams == null ? "" : asciiParams;

				keyDir.rawKeys = new GeoKeyRaw[keyDir.rawHeader.keyCount];

				for (int ki = 0, di = 4; ki < keyDir.rawKeys.Length; ki++, di += 4) {
					if (di >= val.Length) {
						Util.Error("  GeoKey parsing of key {0}/{1} ran off the end of the incoming data array",
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

					switch (valueSourceTag) {
					case GeoTiffTag.NONE:
						if (valCount != 1) {
							Util.Error("  GeoKey[{0}] {1} (ushort) valCount is {2}, only 1 value per key supported.", ki, key, valCount);
						}
						keyDir.addCodeValue(key, valOffset);
						break;
					case GeoTiffTag.GEODOUBLEPARAMSTAG:
						if (valCount != 1) {
							Util.Error("  GeoKey[{0}] {1} (double) valCount is {2}, only 1 value per key supported.", ki, key, valCount);
						}
						keyDir.addDoubleValue(key, doubleParams[valOffset]);
						break;
					case GeoTiffTag.GEOASCIIPARAMSTAG:
						if (valCount < 1) {
							Util.Error("  GeoKey[{0}] {1} (ascii) valCount is {2}, must be >= 1.", ki, key, valCount);
						}
						// valOffset is index into string buffer with all values separated by |
						// valCount includes the terminating |
						string asciiVal = asciiParams.Substring(valOffset, valCount - 1);
						keyDir.addAsciiValue(key, asciiVal);
						break;
					default:
						Util.Error("  GeoKey[{0}] {1} tiffTaglLocation {2} is invalid. Must be 0, GEODOUBLEPARAMSTAG or GEOASCIIPARAMSTAG", ki, key, valueSourceTag);
						break;
					}
				}

				return keyDir;
			}

			return null;
		}

		public static TiePoint[] GetModelTiePoints(Tiff tif) {
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

		public static VectorD3 GetModelPixelScale(Tiff tif) {
			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.MODELPIXELSCALETAG);
			if (v?.Length == 2 && v[0].ToInt() == 3) {
				double[] arr = v[1].ToDoubleArray();
				var val = new VectorD3 { x = arr[0], y = arr[1], z = arr[2] };
				return val;
			}
			return new VectorD3 { x = 0, y = 0, z = 0 };
		}


		GeoKeyDir() {
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

		public ProjectedCSType projectedCSType = ProjectedCSType.Undefined;
		public ProjectionCode projectionCode = ProjectionCode.Undefined;
		public CoordTransformCode coordTransformCode = CoordTransformCode.Undefined;
		public LinearUnitCode projLinearUnit = LinearUnitCode.Undefined;

		public VerticalCSType verticalCSType = VerticalCSType.Undefined;
		public LinearUnitCode verticalLinearUnit = LinearUnitCode.Undefined;

		public string gtCitation = "";
		public string geogCitation = "";
		public string pcsCitation = "";
		public string verticalCitation = "";

		public double geogPrimeMeridianLon = -1.0;
		public double geogLinearUnitSize = 0.0;
		public double geogAngularUnitSize = 0.0;
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

		bool addCodeValue(GeoKey key, ushort value) {
			codeValues.Add(key, value);

			switch (key) {
			case GeoKey.GTModelType:
				modelType = (ModelType)value;
				checkKeyAssignment(key, modelType);
				break;
			case GeoKey.GTRasterType:
				rasterType = (RasterType)value;
				checkKeyAssignment(key, rasterType);
				break;
			case GeoKey.GeogType:
				geographicCSType = (GeographicCSType)value;
				checkKeyAssignment(key, geographicCSType);
				break;
			case GeoKey.GeogGeodeticDatum:
				geodeticDatumCode = (GeodeticDatumCode)value;
				checkKeyAssignment(key, geodeticDatumCode);
				break;
			case GeoKey.GeogPrimeMeridian:
				geogPrimeMeridianCode = (PrimeMeridianCode)value;
				checkKeyAssignment(key, geogPrimeMeridianCode);
				break;
			case GeoKey.GeogLinearUnits:
				geogLinearUnit = (LinearUnitCode)value;
				checkKeyAssignment(key, geogLinearUnit);
				break;
			case GeoKey.GeogAngularUnits:
				geogAngularUnit = (AngularUnitCode)value;
				checkKeyAssignment(key, geogAngularUnit);
				break;
			case GeoKey.GeogEllipsoid:
				geogEllipsoid = (EllipsoidCode)value;
				checkKeyAssignment(key, geogEllipsoid);
				break;
			case GeoKey.GeogAzimuthUnits:
				geogAzimuthUnit = (AngularUnitCode)value;
				checkKeyAssignment(key, geogAzimuthUnit);
				break;
			case GeoKey.ProjectedCSType:
				projectedCSType = (ProjectedCSType)value;
				checkKeyAssignment(key, projectedCSType);
				break;
			case GeoKey.Projection:
				projectionCode = (ProjectionCode)value;
				checkKeyAssignment(key, projectionCode);
				break;
			case GeoKey.ProjCoordTrans:
				coordTransformCode = (CoordTransformCode)value;
				checkKeyAssignment(key, coordTransformCode);
				break;
			case GeoKey.VerticalCSType:
				verticalCSType = (VerticalCSType)value;
				checkKeyAssignment(key, verticalCSType);
				break;
			case GeoKey.VerticalUnits:
				verticalLinearUnit = (LinearUnitCode)value;
				checkKeyAssignment(key, verticalLinearUnit);
				break;
			case GeoKey.ProjLinearUnits:
				projLinearUnit = (LinearUnitCode)value;
				checkKeyAssignment(key, projLinearUnit);
				break;
			default:
				Util.Warn("  unhandled key {0} = code value {1}", key, value);
				return false;
			}

			return true;
		}

		bool addAsciiValue(GeoKey key, string value) {
			asciiValues.Add(key, value);
			switch (key) {
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
				Util.Warn("  unhandled key {0} = string value {1}", key, value);
				return false;
			}

			Util.Log("  {0} = {1}", key, value);

			return true;
		}

		bool addDoubleValue(GeoKey key, double value) {
			doubleValues.Add(key, value);

			switch (key) {
			case GeoKey.GeogPrimeMeridianLon:
				geogPrimeMeridianLon = value;
				break;
			case GeoKey.GeogLinearUnitSize:
				geogLinearUnitSize = value;
				break;
			case GeoKey.GeogAngularUnitSize:
				geogAngularUnitSize = value;
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
				Util.Warn("  unhandled key {0} = double value {1}", key, value);
				return false;
			}

			Util.Log("  {0} = {1}", key, value);

			return true;
		}

		public GeoKeyDirRaw rawHeader = new GeoKeyDirRaw();
		public GeoKeyRaw[] rawKeys = new GeoKeyRaw[0];
		public double[] doubleParams = new double[0];
		public string asciiParams = "";

		public Dictionary<GeoKey, ushort> codeValues = new Dictionary<GeoKey, ushort>();
		public Dictionary<GeoKey, string> asciiValues = new Dictionary<GeoKey, string>();
		public Dictionary<GeoKey, double> doubleValues = new Dictionary<GeoKey, double>();

		static double[] getGeoDoublesParams(Tiff tif) {
			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.GEODOUBLEPARAMSTAG);
			if (v?.Length == 2 && v[0].ToInt() > 0) {
				var val = v[1].ToDoubleArray();
				return val;
			}
			return new double[0];
		}

		static string getGeoAsciiParams(Tiff tif) {
			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.GEOASCIIPARAMSTAG);
			if (v?.Length >= 2) {
				string ascii = v[1].ToString();
				return ascii;
			}
			return null;
		}

		static bool checkKeyAssignment<E>(GeoKey k, E v) where E : struct {
			if (Enum.IsDefined(typeof(E), v)) {
				Util.Log("  {0} = {1}", k, v);
				return true;
			}
			Util.Warn("  {0} = unknown {1} value {2}", k, typeof(E).Name, v);
			return false;
		}
	}
}
