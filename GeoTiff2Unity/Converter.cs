using System;
using System.IO;
using BitMiracle.LibTiff.Classic;

//
// TODO: instead of resizing and just cutting off one piece
// break into multiple squares that suit unity requirements.
// number as grid. each raw height map should be 2^N+1 square with size in name on output
// grid number should also be in output name
// same for RGB tifs out - size not needed, grid coords yes.
//

namespace GeoTiff2Unity {
	class GeoTiffHeader {
		public VectorD2 sizePix = (VectorD2)0;
		public int channelCount = 0;
		public int bitsPerChannel = 0;
		public int rowsPerStrip = 0;
		public Orientation orientation = Orientation.TOPLEFT;
		public SampleFormat sampleFormat = SampleFormat.UINT;
		public VectorD3 pixToProjScale = (VectorD3)0;
		public TiePoint[] tiePoints = null;
		public GeoKeyDir geoKeys = null;
	}

	public static class RasterExt {
		public static VectorD2 GetSizePix<T>(this Raster<T> r) where T : struct {
			return new VectorD2 { x = r.width, y = r.height };
		}

		public static void Init<T>(this Raster<T> r, VectorD2 sizePix) where T : struct {
			r.Init((uint)sizePix.x, (uint)sizePix.y);
		}

		public static Raster<T> Clone<T>(this Raster<T> r, VectorD2 origin, VectorD2 sizePix) where T : struct {
			return r.Clone((uint)origin.x, (uint)origin.y, (uint)sizePix.x, (uint)sizePix.y);
		}

		public static Raster<float> Scaled(this Raster<float> src, VectorD2 sizePix) {
			return src.Scaled((uint)sizePix.x, (uint)sizePix.y);
		}

		public static Raster<ColorF32> Scaled(this Raster<ColorF32> src, VectorD2 sizePix) {
			return src.Scaled((uint)sizePix.x, (uint)sizePix.y);
		}
	}

	public class Converter {
		public string hmTiffInPath = null;
		public string rgbTiffInPath = null;
		public string outputRawHeightPath = null;
		public string outputRGBTifPath = null;

		public bool Go() {
			// we just want to catch exception in the debugger.
#if DEBUG
			go();
			return true;
#else
			try {
				go();
				return true;
			} catch (Exception ex) {
				Util.Log("Exception caught:\n{0}\n{1}", ex.Message, ex.StackTrace);
			}
			return false;
#endif
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

		const uint kMaxUnityTexSize = 8 * 1024;

		Tiff hmTiffIn = null;
		GeoTiffHeader hmHeader = new GeoTiffHeader();

		VectorD2 hmCropSizePix = (VectorD2)0;
		VectorD2 hmOutSizePix = (VectorD2)0;
		uint hmOutBPP = 0;

		float hmMinVal = 0;
		float hmMaxVal = 0;
		float hmNoDataValue = 0;
		float hmF32ToU16SampleTrans = 0;
		float hmF32ToU16SampleScale = 0;

		Tiff rgbTiffIn = null;
		GeoTiffHeader rgbHeader = new GeoTiffHeader();

		VectorD2 rgbCropSizePix = (VectorD2)0;
		VectorD2 rgbOutSizePix = (VectorD2)0;
		uint rgbOutBPP = 0;

		VectorD2 hmToRGBPixScale = (VectorD2)0;
		VectorD2 rgbToHMPixScale = (VectorD2)0;
		//VectorD2 hmToRGBPixTrans = (VectorD2)0;
		//VectorD2 rgbToHMPixTrans = (VectorD2)0;

		static Tiff loadGeoTiffHeader(GeoTiffHeader hdr, string path) {
			Util.Log("Loading header data for {0}", path);
			var tiff = Tiff.Open(path, "r");

			using (var stdout = Console.OpenStandardOutput()) {
				tiff.PrintDirectory(stdout, TiffPrintFlags.NONE);
			}
			Console.WriteLine("");

			hdr.sizePix.x = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
			hdr.sizePix.y = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
			hdr.channelCount = tiff.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
			hdr.bitsPerChannel = tiff.GetField(TiffTag.BITSPERSAMPLE)[0].ToInt();
			hdr.orientation = (Orientation)tiff.GetField(TiffTag.ORIENTATION)[0].ToInt();

			hdr.sampleFormat = SampleFormat.UINT;

			var sampleFormatField = tiff.GetField(TiffTag.SAMPLEFORMAT);
			if (sampleFormatField != null) {
				hdr.sampleFormat = (SampleFormat)sampleFormatField[0].ToInt();
			}

			hdr.pixToProjScale = GeoKeyDir.GetModelPixelScale(tiff);
			hdr.tiePoints = GeoKeyDir.GetModelTiePoints(tiff);
			hdr.geoKeys = GeoKeyDir.GetGeoKeyDir(tiff);
			hdr.rowsPerStrip = tiff.IsTiled() ? 0 : tiff.GetField(TiffTag.ROWSPERSTRIP)[0].ToInt();

			if (hdr.sizePix.Min() < 0) {
				Util.Error("{0} has invalid image size {1}", path, hdr.sizePix);
			}

			Util.Log("");

			return tiff;
		}

		static void loadPixelData<T>(ref Tiff tiff, GeoTiffHeader hdr, Raster<T> raster) where T : struct {
			Util.Log("Loading pixel data for {0}", tiff.FileName());

			raster.Init(hdr.sizePix);

			bool shouldByteSwapStrip = (hdr.bitsPerChannel == 32 && hdr.channelCount == 1 && tiff.IsByteSwapped());

			if (hdr.rowsPerStrip > 0) {
				var tmpStrip = new byte[raster.pitch * hdr.rowsPerStrip];

				for (int y = 0, s = 0, h = (int)hdr.sizePix.y; y < h; y += hdr.rowsPerStrip, s++) {
					int readByteCount = tiff.ReadEncodedStrip(s, tmpStrip, 0, -1);
					int readRowCount = readByteCount / (int)raster.pitch;

					if ( readRowCount * raster.pitch != readByteCount ||
							 (readRowCount < hdr.rowsPerStrip && (y + hdr.rowsPerStrip < h))) 
					{
						Util.Error("tiff image {0} corrupted.", tiff.FileName());
					}

					if (shouldByteSwapStrip) {
						Util.ByteSwap4(tmpStrip);
					}

					raster.SetRawRows((uint)y, tmpStrip, (uint)readRowCount);
				}
			} else {
				Util.Error("{0} is tiled - only stripped images supported.", tiff.FileName());
			}

			tiff.Dispose();
			tiff = null;
		}

		void loadHeightMapHeader() {
			hmTiffIn = loadGeoTiffHeader(hmHeader, hmTiffInPath);

			if (hmHeader.channelCount != 1 || hmHeader.sampleFormat != SampleFormat.IEEEFP || hmHeader.bitsPerChannel != 32) {
				Util.Error("Height map has {0} {1} bit {2} channels. Expected exactly 1 32 bit IEEEFP.",
					hmHeader.channelCount, hmHeader.bitsPerChannel, hmHeader.sampleFormat);
			}
		}

		void loadRGBHeader() {
			rgbTiffIn = loadGeoTiffHeader(rgbHeader, rgbTiffInPath);

			if (rgbHeader.channelCount != 3 || rgbHeader.sampleFormat != SampleFormat.UINT || rgbHeader.bitsPerChannel != 8) {
				Util.Error("{0} has {1} {2} bit {3} channels. Expected 3 8 bit UINT.",
					rgbTiffInPath, rgbHeader.channelCount, rgbHeader.bitsPerChannel, rgbHeader.sampleFormat);
			}
		}

		Raster<float> processHeightMapData(Raster<float> hmRasterF32) {
			{
				int noDataCount = 0;
				for (int i = 0; i < hmRasterF32.pixels.Length; i++) {
					if (hmRasterF32.pixels[i] == hmNoDataValue) {
						hmRasterF32.pixels[i] = (float)hmMinVal;
						noDataCount++;
					}
				}
				if (noDataCount > 0) {
					Util.Warn("Replaced {0} no-data pixels with minValue {1}", noDataCount, hmMinVal);
				}
			}

			// crop to bottom right (for now)
			hmRasterF32 = hmRasterF32.Clone(hmRasterF32.GetSizePix() - hmCropSizePix, hmCropSizePix);

			if (hmOutSizePix != hmCropSizePix) {
				hmRasterF32 = hmRasterF32.Scaled(hmOutSizePix);
			}

			hmMinVal = float.MaxValue;
			hmMaxVal = float.MinValue;
			for (int i = 0; i < hmRasterF32.pixels.Length; i++) {
				var p = hmRasterF32.pixels[i];
				hmMinVal = Math.Min(hmMinVal, p);
				hmMaxVal = Math.Max(hmMaxVal, p);
			}

			return hmRasterF32;
		}

		Raster<ColorU8> processRGBData(Raster<ColorU8> rgbRaster) {
			Util.Log("Trimming RGB image to {0} to match height map.", rgbCropSizePix);

			rgbRaster = rgbRaster.Clone(rgbRaster.GetSizePix() - rgbCropSizePix, rgbCropSizePix);

			// do the easy 2:1 reductions in integer space
			while (rgbRaster.width >= rgbOutSizePix.x * 2) {
				Util.Log("RGB image size {0} exceeds out size {1} >= 2:1. Halving to {2}",
					rgbRaster.GetSizePix(), rgbOutSizePix, (rgbRaster.GetSizePix() / 2).Truncate());
				rgbRaster = rgbRaster.ScaledDown2to1();
			}

			// do the last fractional scaling down in float space
			if (rgbRaster.GetSizePix() != rgbOutSizePix) {
				Util.Log("Scaling RGB image from {0} to {1}.", rgbRaster.GetSizePix(), rgbOutSizePix);
				rgbRaster.Convert(new Raster<ColorF32>()).Scaled(rgbOutSizePix).Convert(rgbRaster);
			}

			return rgbRaster;
		}

		void writeHMRawOut(Raster<ushort> hmRasterU16) {
			// Unity's default height map orientation is upside down WRT RGB textures.
			hmRasterU16.YFlip();

			using (var outFile = new FileStream(outputRawHeightPath, FileMode.Create, FileAccess.Write)) {
				outFile.Write(hmRasterU16.ToByteArray(), 0, (int)hmRasterU16.sizeBytes);
			}
		}

		void writeRGBTiffOut(Raster<ColorU8> rgbRaster) {
			using (Tiff outRGB = Tiff.Open(outputRGBTifPath, "w")) {
				outRGB.SetField(TiffTag.IMAGEWIDTH, (int)rgbRaster.width);
				outRGB.SetField(TiffTag.IMAGELENGTH, (int)rgbRaster.height);
				outRGB.SetField(TiffTag.SAMPLESPERPIXEL, 3);
				outRGB.SetField(TiffTag.BITSPERSAMPLE, rgbRaster.bitsPerPixel / 3);
				outRGB.SetField(TiffTag.SAMPLEFORMAT, SampleFormat.UINT);
				outRGB.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
				outRGB.SetField(TiffTag.ORIENTATION, rgbHeader.orientation);
				outRGB.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
				outRGB.SetField(TiffTag.COMPRESSION, Compression.LZW);
				outRGB.SetField(TiffTag.PREDICTOR, Predictor.HORIZONTAL);

				const uint rgbOutRowsPerStrip = 32;

				outRGB.SetField(TiffTag.ROWSPERSTRIP, (int)rgbOutRowsPerStrip);

				var tmpStrip = new byte[rgbRaster.pitch * rgbOutRowsPerStrip];

				for (uint y = 0, si = 0; y < rgbRaster.height; y += rgbOutRowsPerStrip, si++ ) {
					uint stripRowCount = Math.Min(rgbOutRowsPerStrip, rgbRaster.height - y);

					rgbRaster.GetRawRows(y, tmpStrip, stripRowCount);
					outRGB.WriteEncodedStrip((int)si, tmpStrip, (int)(stripRowCount * rgbRaster.pitch));
				}
			}
		}

		void processHeightMap() {
			{
				var hmRasterF32 = new Raster<float>();
				var hmRasterU16 = new Raster<ushort>();
				hmOutBPP = hmRasterU16.bitsPerPixel;

				loadPixelData(ref hmTiffIn, hmHeader, hmRasterF32);

				Util.Log("Converting float32 height map to uint16 raw Unity asset.");

				hmRasterF32 = processHeightMapData(hmRasterF32);

				hmF32ToU16SampleTrans = (float)-hmMinVal;
				hmF32ToU16SampleScale = (float)((Math.Pow(2.0, hmRasterU16.bitsPerPixel) - 1) / (hmMaxVal + hmF32ToU16SampleTrans));

				hmRasterF32.Convert(hmRasterU16, hmF32ToU16SampleTrans, hmF32ToU16SampleScale);

				writeHMRawOut(hmRasterU16);
			}

			{
				VectorD2 projSizeM = hmOutSizePix * (VectorD2)hmHeader.pixToProjScale;

				float projMinV = (float)(hmMinVal * hmHeader.pixToProjScale.z);
				float projMaxV = (float)(hmMaxVal * hmHeader.pixToProjScale.z);

				Util.Log("");
				Util.Log("Ouput Raw Height Map: {0}", outputRawHeightPath);
				Util.Log("  Dimensions: {0} {1} bit pix", hmOutSizePix, hmOutBPP);
				Util.Log("  Pix value range: {0} (0->{0})", (uint)((hmMaxVal + hmF32ToU16SampleTrans) * hmF32ToU16SampleScale + 0.5f));
				Util.Log("  Pix to geo proj scale: {0}", hmHeader.pixToProjScale);
				Util.Log("  Geo proj size: {0} {1}", projSizeM, hmHeader.geoKeys.projLinearUnit);
				Util.Log("  Geo vertical range: {0} ({1}->{2}) {3}", projMaxV - projMinV, projMinV, projMaxV, hmHeader.geoKeys.verticalLinearUnit);
				Util.Log("");
			}
		}

		void processRGBImage() {
			Util.Log("Converting RGB image to Unity ready asset.");

			{
				var rgbRaster = new Raster<ColorU8>();

				rgbOutBPP = rgbRaster.bitsPerPixel;

				loadPixelData(ref rgbTiffIn, rgbHeader, rgbRaster);

				rgbRaster = processRGBData(rgbRaster);

				writeRGBTiffOut(rgbRaster);
			}

			{
				VectorD2 projSizeM = rgbOutSizePix * (VectorD2)rgbHeader.pixToProjScale;

				Util.Log("");
				Util.Log("Output RGB Image: {0}", outputRGBTifPath);
				Util.Log("  Dimensions: {0} {1} bit pix", rgbOutSizePix, rgbOutBPP);
				Util.Log("  Pix to geo proj scale: {0}", (VectorD2)rgbHeader.pixToProjScale);
				Util.Log("  Geo proj size: {0} {1}", projSizeM, hmHeader.geoKeys.projLinearUnit);
				Util.Log("");
			}
		}

		void alignHMToRGB() {
			if (hmHeader.geoKeys.projLinearUnit != hmHeader.geoKeys.verticalLinearUnit) {
				Util.Error("Mismatch between height map plane units {0} and height map vertical units {1}", hmHeader.geoKeys.projLinearUnit, hmHeader.geoKeys.verticalLinearUnit);
			}

			if (hmHeader.geoKeys.projLinearUnit != rgbHeader.geoKeys.projLinearUnit) {
				Util.Error("Mismatch between height map units {0} and rgb image units {1}", hmHeader.geoKeys.projLinearUnit, rgbHeader.geoKeys.projLinearUnit);
			}

			// Unity requires square height maps
			hmOutSizePix = (VectorD2)hmHeader.sizePix.Min();

			// Unity height maps must be size 2^N + 1 
			hmOutSizePix = (VectorD2)(Math.Pow(2.0, Math.Ceiling(Math.Log(hmOutSizePix.x - 1)/Math.Log(2.0))) + 1.0);

			hmToRGBPixScale = hmHeader.pixToProjScale / rgbHeader.pixToProjScale;
			rgbToHMPixScale = rgbHeader.pixToProjScale / hmHeader.pixToProjScale;

			if (rgbHeader.tiePoints.Length != hmHeader.tiePoints.Length) {
				Util.Warn("Input height map has {0} tie points, rgb image has {1}", hmHeader.tiePoints.Length, rgbHeader.tiePoints.Length);
			}

			if (Math.Max(rgbHeader.tiePoints.Length, hmHeader.tiePoints.Length) > 1) {
				Util.Warn("Tie points beyond index 0 ignored.");
			}

			if (rgbHeader.tiePoints[0].rasterPt != hmHeader.tiePoints[0].rasterPt) {
				Util.Error("height map raster tie point {0} does not match rgb image raster tie point {1}",
					(VectorD2)hmHeader.tiePoints[0].rasterPt, (VectorD2)rgbHeader.tiePoints[0].rasterPt);
			}

			var tiePointDelta = (VectorD2)(rgbHeader.tiePoints[0].modelPt - hmHeader.tiePoints[0].modelPt);

			Util.Log("Tie Points:");
			Util.Log("  Height Map: {0} {1}", hmHeader.tiePoints[0], hmHeader.geoKeys.rasterType);
			Util.Log("  RGB Image: {0} {1}", rgbHeader.tiePoints[0], rgbHeader.geoKeys.rasterType);
			Util.Log("  Proj Point Delta: {0} {1}", tiePointDelta, hmHeader.geoKeys.projLinearUnit);
			Util.Log("  Proj Point HM Pix Delta: {0}", tiePointDelta / (VectorD2)hmHeader.pixToProjScale);
			Util.Log("  Proj Point RGB Pix Delta: {0}", tiePointDelta / (VectorD2)rgbHeader.pixToProjScale);

			// misalignment seen is within bounds of 1/2 height map pixel - accounted for by diff in raster types - one is pixIsArea the other pixIsPoint.
			// see raster space doc: http://geotiff.maptools.org/spec/geotiff2.5.html
#if false
			//
			// TODO: take raster space pixel is point vs pixel is area into account when computing alignment.
			//
			if (rgbHeader.tiePoints[0].modelPt != hmTiffHeader.tiePoints[0].modelPt) {
				VectorD2 alignmentOffset = rgbHeader.tiePoints[0].modelPt - hmTiffHeader.tiePoints[0].modelPt;

				// TODO: find out if pixel row y+ corresponds to world space y+ (north)
				// or if image is displayed north up (y+ is south and therefor negative)
				if (rgbHeader.tiePoints[0].rasterPt.y == 0) {
					alignmentOffset.y *= -1;
				}

				alignmentOffset *= (VectorD2)hmToRGBPixScale;

				if(alignmentOffset.x < 0) {
					rgbToHMPixTrans.x = -alignmentOffset.x;
				} else {
					hmToRGBPixTrans.x = alignmentOffset.x;
				}

				if (alignmentOffset.y < 0) {
					rgbToHMPixTrans.y = -alignmentOffset.y;
				} else {
					hmToRGBPixTrans.y = alignmentOffset.y;
				}

				//
				// TODO: verify above is correct
				//
			}

			//
			// TODO:
			// if either of the hmToRGBPixTrans or rgbToHMPixTrans are non-zero
			// trim one at the top and the other at the bottom to get them to line up vertically.
			// and trim one at the left and the other at the right to get them to line up horizontally.
			//
#endif // false

			hmCropSizePix = hmOutSizePix;

			if (hmCropSizePix.x > hmHeader.sizePix.x || hmCropSizePix.y > hmHeader.sizePix.y) {
				hmCropSizePix = (VectorD2)hmHeader.sizePix.Min();
				hmHeader.pixToProjScale *= (hmCropSizePix / hmOutSizePix);
			}

			rgbCropSizePix = (hmCropSizePix * (VectorD2)hmToRGBPixScale).Ceiling();

			rgbOutSizePix = rgbCropSizePix;

			if ( rgbOutSizePix.Max() > kMaxUnityTexSize ) {
				double outScale = kMaxUnityTexSize / rgbOutSizePix.Max();
				rgbOutSizePix = (rgbOutSizePix * outScale).Ceiling();
				rgbHeader.pixToProjScale /= outScale;
			}
		}

		void go() {
			loadHeightMapHeader();

			loadRGBHeader();

			alignHMToRGB();

			processHeightMap();

			processRGBImage();
		}
	}
}
