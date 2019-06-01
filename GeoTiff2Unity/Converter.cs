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
// TODO: for intermed test to fix unity import misalignment
// go back to cutting at 1025x1025 or scale up 1968 to 2049

namespace GeoTiff2Unity {
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

		Tiff hmTiffIn = null;
		VectorD2 hmSizePix = (VectorD2)0;
		int hmChannelCount = 0;
		int hmRowsPerStrip = 0;
		Orientation hmOrientation = Orientation.TOPLEFT;
		SampleFormat hmSampleFormat = SampleFormat.UINT;
		VectorD3 hmPixToProjScale = (VectorD3)0;
		TiePoint[] hmTiePoints = null;
		GeoKeyDir hmGeoKeys = null;

		VectorD2 hmOutSizePix = (VectorD2)0;
		uint hmOutBPP = 0;

		float hmMinVal = 0;
		float hmMaxVal = 0;
		float hmNoDataValue = 0;
		float hmF32ToU16SampleTrans = 0;
		float hmF32ToU16SampleScale = 0;

		Tiff rgbTiffIn = null;
		VectorD2 rgbSizePix = (VectorD2)0;
		int rgbChannelCount = 0;
		int rgbRowsPerStrip = 0;
		Orientation rgbOrientation = Orientation.TOPLEFT;
		SampleFormat rgbSampleFormat = SampleFormat.UINT;
		VectorD3 rgbPixToProjScale = (VectorD3)0;
		TiePoint[] rgbTiePoints = null;
		GeoKeyDir rgbGeoKeys = null;

		VectorD2 rgbOutSizePix = (VectorD2)0;
		uint rgbOutBPP = 0;

		VectorD2 hmToRGBPixScale = (VectorD2)0;
		VectorD2 rgbToHMPixScale = (VectorD2)0;
		VectorD2 hmToRGBPixTrans = (VectorD2)0;
		VectorD2 rgbToHMPixTrans = (VectorD2)0;

		void loadHeightMapHeader() {
			Util.Log("Loading header data for {0}", hmTiffInPath);
			hmTiffIn = Tiff.Open(hmTiffInPath, "r");
			using (var stdout = Console.OpenStandardOutput()) {
				hmTiffIn.PrintDirectory(stdout, TiffPrintFlags.NONE);
			}
			Console.WriteLine("");

			hmSizePix.x = hmTiffIn.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
			hmSizePix.y = hmTiffIn.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
			hmRowsPerStrip = hmTiffIn.IsTiled() ? 0 : hmTiffIn.GetField(TiffTag.ROWSPERSTRIP)[0].ToInt();
			hmOrientation = (Orientation)hmTiffIn.GetField(TiffTag.ORIENTATION)[0].ToInt();

			if (hmSizePix.x <= 0 || hmSizePix.y <= 0) {
				Util.Error("Invalid height map image size {0}", hmSizePix);
			}

			hmChannelCount = hmTiffIn.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
			hmSampleFormat = (SampleFormat)hmTiffIn.GetField(TiffTag.SAMPLEFORMAT)[0].ToInt();

			if (hmChannelCount != 1 || hmSampleFormat != SampleFormat.IEEEFP) {
				Util.Error("Height map has {0} {1} channels. Should be exactly 1 UINT.",
					hmChannelCount, hmSampleFormat);
			}

			hmPixToProjScale = GeoKeyDir.GetModelPixelScale(hmTiffIn);
			hmTiePoints = GeoKeyDir.GetModelTiePoints(hmTiffIn);
			hmGeoKeys = GeoKeyDir.GetGeoKeyDir(hmTiffIn);

			hmMinVal = (float)hmTiffIn.GetField(TiffTag.SMINSAMPLEVALUE)[0].ToDouble();
			hmMaxVal = (float)hmTiffIn.GetField(TiffTag.SMAXSAMPLEVALUE)[0].ToDouble();
			hmNoDataValue = (float)getGdalNoData(hmTiffIn);

			Util.Log("");
		}

		void loadRGBHeader() {
			Util.Log("Loading header data for {0}", rgbTiffInPath);
			rgbTiffIn = Tiff.Open(rgbTiffInPath, "r");

			using (var stdout = Console.OpenStandardOutput()) {
				rgbTiffIn.PrintDirectory(stdout, TiffPrintFlags.NONE);
			}
			Console.WriteLine("");

			rgbSizePix.x = rgbTiffIn.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
			rgbSizePix.y = rgbTiffIn.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
			rgbChannelCount = rgbTiffIn.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
			rgbOrientation = (Orientation)rgbTiffIn.GetField(TiffTag.ORIENTATION)[0].ToInt();

			rgbSampleFormat = SampleFormat.UINT;

			var sampleFormatField = rgbTiffIn.GetField(TiffTag.SAMPLEFORMAT);
			if (sampleFormatField != null) {
				rgbSampleFormat = (SampleFormat)sampleFormatField[0].ToInt();
			}

			if (rgbChannelCount != 3 || rgbSampleFormat != SampleFormat.UINT) {
				Util.Error("{0} has {1} {2} channels. Only 3 UINT supported.", rgbTiffInPath, rgbChannelCount, rgbSampleFormat);
			}

			rgbPixToProjScale = GeoKeyDir.GetModelPixelScale(rgbTiffIn);
			rgbTiePoints = GeoKeyDir.GetModelTiePoints(rgbTiffIn);
			rgbGeoKeys = GeoKeyDir.GetGeoKeyDir(rgbTiffIn);
			rgbRowsPerStrip = rgbTiffIn.IsTiled() ? 0 : rgbTiffIn.GetField(TiffTag.ROWSPERSTRIP)[0].ToInt();

			if (rgbSizePix.x <= 0 || rgbSizePix.y <= 0) {
				Util.Error("Invalid rgb image size {0}", rgbSizePix);
			}

			Util.Log("");
		}

		void loadHeightMapData(Raster<float> hmRasterF32) {
			Util.Log("Loading pixel data for {0}", hmTiffInPath);

			hmRasterF32.Init(hmSizePix);

			bool isByteSwapped = hmTiffIn.IsByteSwapped();

			if (hmRowsPerStrip > 0) {
				var tmpStrip = new byte[hmRasterF32.pitch * hmRowsPerStrip];

				for (int y = 0, s = 0, h = (int)hmSizePix.y; y < h; y += hmRowsPerStrip, s++) {
					int readByteCount = hmTiffIn.ReadEncodedStrip(s, tmpStrip, 0, -1);
					int readRowCount = readByteCount / (int)hmRasterF32.pitch;

					if (readRowCount == 0 ||
								readRowCount * hmRasterF32.pitch != readByteCount ||
								(readRowCount < hmRowsPerStrip && (y + hmRowsPerStrip < h))) {
						Util.Error("input height map corrupted.");
					}

					if (isByteSwapped) {
						Util.ByteSwap4(tmpStrip);
					}

					hmRasterF32.SetRawRows((uint)y, tmpStrip, (uint)readRowCount);
				}
			} else {
				int hmTileW = hmTiffIn.GetField(TiffTag.TILEWIDTH)[0].ToInt();
				int hmTileH = hmTiffIn.GetField(TiffTag.TILELENGTH)[0].ToInt();

				if (hmTileW < hmSizePix.x || hmTileH < hmSizePix.y) {
					Util.Error("Input heigh tmap is multi-tiled. Only stripped and single tile images supported.");
				}

				var tmpTile = new byte[hmRasterF32.sizeBytes];
				hmTiffIn.ReadEncodedTile(0, tmpTile, 0, tmpTile.Length);
				if (isByteSwapped) {
					Util.ByteSwap4(tmpTile);
				}
				hmRasterF32.SetRawRows(0, tmpTile, (uint)hmSizePix.y);
			}

			hmTiffIn.Dispose();
			hmTiffIn = null;
		}

		void loadRGBData(Raster<ColorU8> rgbRaster) {
			Util.Log("Loading pixel data for {0}", rgbTiffInPath);

			rgbRaster.Init(rgbSizePix);

			if (rgbRowsPerStrip > 0) {
				var tmpStrip = new byte[rgbRaster.pitch * rgbRowsPerStrip];

				for (uint y = 0, s = 0, h = (uint)rgbSizePix.y; y < h; y += (uint)rgbRowsPerStrip, s++) {
					int readByteCount = rgbTiffIn.ReadEncodedStrip((int)s, tmpStrip, 0, -1);
					int readRowCount = (int)(readByteCount / rgbRaster.pitch);

					if (readRowCount == 0 ||
							readRowCount * rgbRaster.pitch != readByteCount ||
							(readRowCount < rgbRowsPerStrip && (y + rgbRowsPerStrip < rgbSizePix.y))) {
						Util.Error("input height map corrupted.");
					}

					rgbRaster.SetRawRows(y, tmpStrip, (uint)readRowCount);
				}
			} else {
				int rgbTileW = rgbTiffIn.GetField(TiffTag.TILEWIDTH)[0].ToInt();
				int rgbTileH = rgbTiffIn.GetField(TiffTag.TILELENGTH)[0].ToInt();

				if (rgbTileW < rgbSizePix.x || rgbTileH < rgbSizePix.y) {
					Util.Error("Input rgb image is multi-tiled. Only stripped and single tile images supported.");
				}

				var tmpTile = new byte[rgbRaster.sizeBytes];
				rgbTiffIn.ReadEncodedTile(0, tmpTile, 0, tmpTile.Length);
				rgbRaster.SetRawRows(0, tmpTile, rgbRaster.height);
			}

			rgbTiffIn.Dispose();
			rgbTiffIn = null;
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
			hmRasterF32 = hmRasterF32.Clone(hmRasterF32.GetSizePix() - hmOutSizePix, hmOutSizePix);

			hmMinVal = float.MaxValue;
			hmMaxVal = float.MinValue;
			for (int i = 0; i < hmRasterF32.pixels.Length; i++) {
				var p = hmRasterF32.pixels[i];
				hmMinVal = Math.Min(hmMinVal, p);
				hmMaxVal = Math.Max(hmMaxVal, p);
			}

			return hmRasterF32;
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
				outRGB.SetField(TiffTag.ORIENTATION, rgbOrientation);
				outRGB.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
				outRGB.SetField(TiffTag.COMPRESSION, Compression.LZW);
				outRGB.SetField(TiffTag.PREDICTOR, Predictor.HORIZONTAL);

				//
				// TODO - change to 32 row strips. this is lazy and ugly.
				// it also causes problems if the image is not well sized.
				// e.g. 8191x8192 image fails to write.
				//
				//outRGB.SetField(TiffTag.ROWSPERSTRIP, 32);
				outRGB.SetField(TiffTag.TILEWIDTH, (int)rgbRaster.width);
				outRGB.SetField(TiffTag.TILELENGTH, (int)rgbRaster.height);


				outRGB.WriteEncodedTile(0, rgbRaster.ToByteArray(), (int)rgbRaster.sizeBytes);
			}
		}

		void processHeightMap() {
			{
				var hmRasterF32 = new Raster<float>();
				var hmRasterU16 = new Raster<ushort>();
				hmOutBPP = hmRasterU16.bitsPerPixel;

				loadHeightMapData(hmRasterF32);

				Util.Log("Converting float32 height map to uint16 raw Unity asset.");

				hmRasterF32 = processHeightMapData(hmRasterF32);

				hmF32ToU16SampleTrans = (float)-hmMinVal;
				hmF32ToU16SampleScale = (float)((Math.Pow(2.0, hmRasterU16.bitsPerPixel) - 1) / (hmMaxVal + hmF32ToU16SampleTrans));

				hmRasterF32.Convert(hmRasterU16, hmF32ToU16SampleTrans, hmF32ToU16SampleScale);

				writeHMRawOut(hmRasterU16);
			}

			{
				VectorD2 projSizeM = hmOutSizePix * (VectorD2)hmPixToProjScale;

				float projMinV = (float)(hmMinVal * hmPixToProjScale.z);
				float projMaxV = (float)(hmMaxVal * hmPixToProjScale.z);

				Util.Log("");
				Util.Log("Ouput Raw Height Map: {0}", outputRawHeightPath);
				Util.Log("  Dimensions: {0} {1} bit pix", hmOutSizePix, hmOutBPP);
				Util.Log("  Pix value range: {0} (0->{0})", (uint)((hmMaxVal + hmF32ToU16SampleTrans) * hmF32ToU16SampleScale + 0.5f));
				Util.Log("  Pix to geo proj scale: {0}", hmPixToProjScale);
				Util.Log("  Geo proj size: {0} {1}", projSizeM, hmGeoKeys.projLinearUnit);
				Util.Log("  Geo vertical range: {0} ({1}->{2}) {3}", projMaxV - projMinV, projMinV, projMaxV, hmGeoKeys.verticalLinearUnit);
				Util.Log("");
			}
		}

		Raster<ColorU8> processRGBData(Raster<ColorU8> rgbRaster) {
			Util.Log("Trimming RGB image to {0} to match height map.", rgbOutSizePix);

			rgbRaster = rgbRaster.Clone(rgbRaster.GetSizePix() - rgbOutSizePix, rgbOutSizePix);

			const uint kMaxUnityTexSize = 8 * 1024;

			// do the easy 2:1 reductions in integer space
			while (rgbRaster.GetSizePix().Max() >= (kMaxUnityTexSize * 2)) {
				Util.Log("RGB image size {0} exceeds {1} max texture size by >= 2:1. Halving to {2}",
					rgbRaster.GetSizePix(), kMaxUnityTexSize, (rgbRaster.GetSizePix() / 2).Truncate());
				rgbRaster = rgbRaster.ScaledDown2to1();
			}

			// do the last fractional scaling down in float space
			if (rgbRaster.GetSizePix().Max() > kMaxUnityTexSize) {
				double scaleDown = (double)kMaxUnityTexSize / rgbRaster.GetSizePix().Max();
				VectorD2 scaledSize = rgbRaster.GetSizePix() * scaleDown;

				Util.Log("RGB image size {0} exceeds {1} max texture size. Scaling to {2}", rgbRaster.GetSizePix(), kMaxUnityTexSize, scaledSize);
				var rgbRasterF32 = rgbRaster.Convert(new Raster<ColorF32>()).Scaled(scaledSize);
				rgbRasterF32.Convert(rgbRaster);
			}

			rgbPixToProjScale *= (VectorD3)(rgbOutSizePix / rgbRaster.GetSizePix());

			rgbPixToProjScale.x *= rgbOutSizePix.x / rgbRaster.width;
			rgbPixToProjScale.y *= rgbOutSizePix.y / rgbRaster.height;
			rgbOutSizePix.x = rgbRaster.width;
			rgbOutSizePix.y = rgbRaster.height;

			return rgbRaster;
		}

		void processRGBImage() {
			Util.Log("Converting RGB image to Unity ready asset.");

			{
				var rgbRaster = new Raster<ColorU8>();

				rgbOutBPP = rgbRaster.bitsPerPixel;

				loadRGBData(rgbRaster);

				rgbRaster = processRGBData(rgbRaster);

				writeRGBTiffOut(rgbRaster);
			}

			{
				VectorD2 projSizeM = rgbOutSizePix * (VectorD2)rgbPixToProjScale;

				Util.Log("");
				Util.Log("Output RGB Image: {0}", outputRGBTifPath);
				Util.Log("  Dimensions: {0} {1} bit pix", rgbOutSizePix, rgbOutBPP);
				Util.Log("  Pix to geo proj scale: {0}", (VectorD2)rgbPixToProjScale);
				Util.Log("  Geo proj size: {0} {1}", projSizeM, hmGeoKeys.projLinearUnit);
				Util.Log("");
			}
		}

		void alignHMToRGB() {
			// HACK FOR NOW - Unity requires square textures?
			hmOutSizePix = (VectorD2)hmSizePix.Min();

			hmToRGBPixScale = hmPixToProjScale / rgbPixToProjScale;
			rgbToHMPixScale = rgbPixToProjScale / hmPixToProjScale;

			if (rgbTiePoints.Length != hmTiePoints.Length) {
				Util.Warn("Input height map has {0} tie points, rgb image has {1}", hmTiePoints.Length, rgbTiePoints.Length);
			}

			if (Math.Max(rgbTiePoints.Length, hmTiePoints.Length) > 1) {
				Util.Warn("Tie points beyond index 0 ignored.");
			}

			if (!rgbTiePoints[0].rasterPt.Eq(hmTiePoints[0].rasterPt)) {
				Util.Error("height map raster tie point {0} does not match rgb image raster tie point {1}",
					(VectorD2)hmTiePoints[0].rasterPt, (VectorD2)rgbTiePoints[0].rasterPt);
			}

			Util.Log("TiePoints:");
			Util.Log("  Height Map: {0} {1}", hmTiePoints[0], hmGeoKeys.rasterType);
			Util.Log("  RGB Image: {0} {1}", rgbTiePoints[0], rgbGeoKeys.rasterType);

			// misalignment seen is within bounds of 1/2 height map pixel - accounted for by diff in raster types - one is pixIsArea the other pixIsPoint.
			// see raster space doc: http://geotiff.maptools.org/spec/geotiff2.5.html
#if false
			//
			// TODO: take raster space pixel is point vs pixel is area into account when computing alignment.
			//
			if (!rgbTiePoints[0].modelPt.Eq(hmTiePoints[0].modelPt)) {
				VectorD2 alignmentOffset = rgbTiePoints[0].modelPt - hmTiePoints[0].modelPt;

				// TODO: find out if pixel row y+ corresponds to world space y+ (north)
				// or if image is displayed north up (y+ is south and therefor negative)
				if (rgbTiePoints[0].rasterPt.y == 0) {
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
			rgbOutSizePix = (hmOutSizePix * (VectorD2)hmToRGBPixScale).Ceiling();
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
