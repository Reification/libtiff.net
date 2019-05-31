using System;
using System.IO;
using BitMiracle.LibTiff.Classic;

namespace GeoTiff2Unity {
	public class Converter {

		public string hmTiffInPath = null;
		public string rgbTiffInPath = null;
		public string outputRawHeightPath = null;
		public string outputRGBTifPath = null;

		public bool Go() {
			//try {
			go();
			return true;
			//} catch (Exception ex) {
			//	Util.Log("Exception caught:\n{0}\n{1}", ex.Message, ex.StackTrace);
			//	throw ex;
			//}
			//return false;
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
		int hmWidth = 0;
		int hmHeight = 0;
		int hmChannelCount = 0;
		int hmRowsPerStrip = 0;
		SampleFormat hmSampleFormat = SampleFormat.UINT;
		VectorD3 hmScale = VectorD3.zero;
		TiePoint[] hmTiePoints = null;
		GeoKeyDir hmGeoKeys = null;

		uint hmOutWidth = 0;
		uint hmOutHeight = 0;
		uint hmOutBPP = 0;

		float hmMinVal = 0;
		float hmMaxVal = 0;
		float hmNoDataValue = 0;
		float hmToRawTranslation = 0;
		float hmToRawScale = 0;

		Tiff rgbTiffIn = null;
		int rgbWidth = 0;
		int rgbHeight = 0;
		int rgbChannelCount = 0;
		int rgbRowsPerStrip = 0;
		SampleFormat rgbSampleFormat = SampleFormat.UINT;
		VectorD3 rgbScale = VectorD3.zero;
		TiePoint[] rgbTiePoints = null;
		GeoKeyDir rgbGeoKeys = null;

		uint rgbOutWidth = 0;
		uint rgbOutHeight = 0;
		uint rgbOutBPP = 0;

		uint hmToRGBScale = 0;
		int hmRGBAlignOffsetX = 0;
		int hmRGBAlignOffsetY = 0;

		void loadHeightMapHeader() {
			Util.Log("Loading header data for {0}", hmTiffInPath);
			hmTiffIn = Tiff.Open(hmTiffInPath, "r");
			using (var stdout = Console.OpenStandardOutput()) {
				hmTiffIn.PrintDirectory(stdout, TiffPrintFlags.NONE);
			}
			Console.WriteLine("");

			hmWidth = hmTiffIn.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
			hmHeight = hmTiffIn.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
			hmRowsPerStrip = hmTiffIn.IsTiled() ? 0 : hmTiffIn.GetField(TiffTag.ROWSPERSTRIP)[0].ToInt();

			if (hmWidth <= 0 || hmHeight <= 0) {
				Util.Error("Invalid height map image size {0}x{1}", hmWidth, hmHeight);
			}

			hmChannelCount = hmTiffIn.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
			hmSampleFormat = (SampleFormat)hmTiffIn.GetField(TiffTag.SAMPLEFORMAT)[0].ToInt();

			if (hmChannelCount != 1 || hmSampleFormat != SampleFormat.IEEEFP) {
				Util.Error("Height map has {0} {1} channels. Should be exactly 1 UINT.",
					hmChannelCount, hmSampleFormat);
			}

			hmScale = GeoKeyDir.GetModelPixelScale(hmTiffIn);
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

			rgbWidth = rgbTiffIn.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
			rgbHeight = rgbTiffIn.GetField(TiffTag.IMAGELENGTH)[0].ToInt();

			rgbChannelCount = rgbTiffIn.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
			rgbSampleFormat = SampleFormat.UINT;

			var sampleFormatField = rgbTiffIn.GetField(TiffTag.SAMPLEFORMAT);
			if (sampleFormatField != null) {
				rgbSampleFormat = (SampleFormat)sampleFormatField[0].ToInt();
			}

			if (rgbChannelCount != 3 || rgbSampleFormat != SampleFormat.UINT) {
				Util.Error("{0} has {1} {2} channels. Only 3 UINT supported.", rgbTiffInPath, rgbChannelCount, rgbSampleFormat);
			}

			rgbScale = GeoKeyDir.GetModelPixelScale(rgbTiffIn);
			rgbTiePoints = GeoKeyDir.GetModelTiePoints(rgbTiffIn);
			rgbGeoKeys = GeoKeyDir.GetGeoKeyDir(rgbTiffIn);
			rgbRowsPerStrip = rgbTiffIn.IsTiled() ? 0 : rgbTiffIn.GetField(TiffTag.ROWSPERSTRIP)[0].ToInt();

			if (rgbWidth <= 0 || rgbHeight <= 0) {
				Util.Error("Invalid rgb image size {0}x{1}", rgbWidth, rgbHeight);
			}

			Util.Log("");
		}

		void loadHeightMapData(Raster<float> hmRasterF32) {
			Util.Log("Loading pixel data for {0}", hmTiffInPath);

			hmRasterF32.Init((uint)hmWidth, (uint)hmHeight);

			bool isByteSwapped = hmTiffIn.IsByteSwapped();

			if (hmRowsPerStrip > 0) {
				var tmpStrip = new byte[hmRasterF32.pitch * hmRowsPerStrip];

				for (int y = 0, s = 0; y < hmHeight; y += hmRowsPerStrip, s++) {
					int readByteCount = hmTiffIn.ReadEncodedStrip(s, tmpStrip, 0, -1);
					int readRowCount = readByteCount / (int)hmRasterF32.pitch;

					if (readRowCount == 0 ||
								readRowCount * hmRasterF32.pitch != readByteCount ||
								(readRowCount < hmRowsPerStrip && (y + hmRowsPerStrip < hmHeight))) {
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

				if (hmTileW < hmWidth || hmTileH < hmHeight) {
					Util.Error("Input heigh tmap is multi-tiled. Only stripped and single tile images supported.");
				}

				var tmpTile = new byte[hmRasterF32.sizeBytes];
				hmTiffIn.ReadEncodedTile(0, tmpTile, 0, tmpTile.Length);
				if (isByteSwapped) {
					Util.ByteSwap4(tmpTile);
				}
				hmRasterF32.SetRawRows(0, tmpTile, (uint)hmHeight);
			}

			hmTiffIn.Dispose();
			hmTiffIn = null;
		}

		void loadRGBData(Raster<ColorU8> rgbRaster) {
			Util.Log("Loading pixel data for {0}", rgbTiffInPath);

			rgbRaster.Init((uint)rgbWidth, (uint)rgbHeight);

			if (rgbRowsPerStrip > 0) {
				var tmpStrip = new byte[rgbRaster.pitch * rgbRowsPerStrip];

				for (uint y = 0, s = 0; y < rgbHeight; y += (uint)rgbRowsPerStrip, s++) {
					int readByteCount = rgbTiffIn.ReadEncodedStrip((int)s, tmpStrip, 0, -1);
					int readRowCount = (int)(readByteCount / rgbRaster.pitch);

					if (readRowCount == 0 ||
							readRowCount * rgbRaster.pitch != readByteCount ||
							(readRowCount < rgbRowsPerStrip && (y + rgbRowsPerStrip < rgbHeight))) {
						Util.Error("input height map corrupted.");
					}

					rgbRaster.SetRawRows(y, tmpStrip, (uint)readRowCount);
				}
			} else {
				int rgbTileW = rgbTiffIn.GetField(TiffTag.TILEWIDTH)[0].ToInt();
				int rgbTileH = rgbTiffIn.GetField(TiffTag.TILELENGTH)[0].ToInt();

				if (rgbTileW < rgbWidth || rgbTileH < rgbHeight) {
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
			// HACK FOR NOW - Unity requires square textures?
			hmOutWidth = hmOutHeight = (uint)Math.Min(hmWidth, hmHeight);

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
			hmRasterF32 = hmRasterF32.Clone(hmRasterF32.width - hmOutWidth,
																				hmRasterF32.height - hmOutHeight,
																				hmOutWidth,
																				hmOutHeight);

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
				outRGB.SetField(TiffTag.BITSPERSAMPLE, 8);
				outRGB.SetField(TiffTag.SAMPLESPERPIXEL, 3);
				outRGB.SetField(TiffTag.SAMPLEFORMAT, SampleFormat.UINT);
				outRGB.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
				outRGB.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
				outRGB.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
				outRGB.SetField(TiffTag.COMPRESSION, Compression.LZW);
				outRGB.SetField(TiffTag.PREDICTOR, Predictor.HORIZONTAL);
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

				hmToRawTranslation = (float)-hmMinVal;
				hmToRawScale = (float)((Math.Pow(2.0, hmRasterU16.bitsPerPixel) - 1) / (hmMaxVal + hmToRawTranslation));

				hmRasterF32.Convert(hmRasterU16, hmToRawTranslation, hmToRawScale);

				writeHMRawOut(hmRasterU16);
			}

			{
				float projW = (float)(hmOutWidth * hmScale.x);
				float projH = (float)(hmOutHeight * hmScale.y);
				float projMinV = (float)(hmMinVal * hmScale.z);
				float projMaxV = (float)(hmMaxVal * hmScale.z);

				Util.Log("");
				Util.Log("Ouput Raw Height Map: {0}", outputRawHeightPath);
				Util.Log("  Dimensions: {0}x{1} {2} bit pix", hmOutWidth, hmOutHeight, hmOutBPP);
				Util.Log("  Pix value range: {1} ({0}->{1})", 0, (uint)((hmMaxVal + hmToRawTranslation) * hmToRawScale + 0.5f));
				Util.Log("  Pix to geo proj scale: {0}", hmScale);
				Util.Log("  Geo proj size: {0}x{1} {2}", projW, projH, hmGeoKeys.projLinearUnit);
				Util.Log("  Geo vertical range: {0} ({1}->{2}) {3}", projMaxV - projMinV, projMinV, projMaxV, hmGeoKeys.verticalLinearUnit);
				Util.Log("");
			}
		}

		Raster<ColorU8> processRGBData(Raster<ColorU8> rgbRaster) {
			hmToRGBScale = (uint)(rgbWidth / hmWidth);
			rgbOutWidth = hmOutWidth * hmToRGBScale;
			rgbOutHeight = hmOutHeight * hmToRGBScale;

			Util.Log("Trimming RGB image to {0}x{1} to match height map.", rgbOutWidth, rgbOutHeight);

			rgbRaster = rgbRaster.Clone(rgbRaster.width - rgbOutWidth,
																		rgbRaster.height - rgbOutHeight,
																		rgbOutWidth,
																		rgbOutHeight);

			const uint kMaxUnityTexSize = 8 * 1024;

			// do the easy 2:1 reductions in integer space
			while (Math.Max(rgbRaster.width, rgbRaster.height) >= (kMaxUnityTexSize * 2)) {
				Util.Log("RGB image size {0}x{1} exceeds 8k max texture size by >= 2:1. Halving to {2}x{3}",
					rgbRaster.width, rgbRaster.height, rgbRaster.width / 2, rgbRaster.height / 2);
				rgbRaster = rgbRaster.ScaledDown2to1();
			}

			// do the last fractional scaling down in float space
			if (Math.Max(rgbRaster.width, rgbRaster.height) > kMaxUnityTexSize) {
				double scaleDown = (double)kMaxUnityTexSize / Math.Max(rgbRaster.width, rgbRaster.height);
				uint scaleDownW = (uint)(scaleDown * rgbRaster.width);
				uint scaleDownH = (uint)(scaleDown * rgbRaster.height);
				Util.Log("RGB image size {0}x{1} exceeds 8k max texture size. Scaling to {0}x{1}", scaleDownW, scaleDownH);
				var rgbRasterF32 = rgbRaster.Convert(new Raster<ColorF32>()).Scaled(scaleDownW, scaleDownH);
				rgbRasterF32.Convert(rgbRaster);
			}

			rgbScale.x *= (double)rgbOutWidth / rgbRaster.width;
			rgbScale.y *= (double)rgbOutHeight / rgbRaster.height;
			rgbOutWidth = rgbRaster.width;
			rgbOutHeight = rgbRaster.height;

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
				float projW = (float)(rgbOutWidth * rgbScale.x);
				float projH = (float)(rgbOutHeight * rgbScale.y);

				Util.Log("");
				Util.Log("Output RGB Image: {0}", outputRGBTifPath);
				Util.Log("  Dimensions: {0}x{1} {2} bit pix", rgbOutWidth, rgbOutHeight, rgbOutBPP);
				Util.Log("  Pix to geo proj scale: {0}", rgbScale);
				Util.Log("  Geo proj size: {0}x{1} {2}", projW, projH, hmGeoKeys.projLinearUnit);
				Util.Log("");
			}
		}

		void go() {
			loadHeightMapHeader();

			loadRGBHeader();

			if (rgbTiePoints.Length != hmTiePoints.Length) {
				Util.Warn("Input height map has {0} tie points, rgb image has {1}", hmTiePoints.Length, rgbTiePoints.Length);
			}

			if (Math.Max(rgbTiePoints.Length, hmTiePoints.Length) > 1) {
				Util.Warn("Tie points beyond index 0 ignored.");
			}

			if (!rgbTiePoints[0].Eq(hmTiePoints[0])) {
				// offset in meters. N > S, E > W
				double alignOffsetY = hmTiePoints[0].modelPt.y - rgbTiePoints[0].modelPt.y;
				double alignOffsetX = rgbTiePoints[0].modelPt.x - hmTiePoints[0].modelPt.x;

				alignOffsetX *= rgbScale.x;
				alignOffsetY *= rgbScale.y;
			}

			processHeightMap();

			processRGBImage();
		}
	}
}
