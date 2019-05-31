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

		void go() {
			Tiff inHeightMap = Tiff.Open(inputFloatHeightTifPath, "r");
			using (var stdout = Console.OpenStandardOutput()) {
				inHeightMap.PrintDirectory(stdout, TiffPrintFlags.NONE);
			}

			int hmWidth = inHeightMap.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
			int hmHeight = inHeightMap.GetField(TiffTag.IMAGELENGTH)[0].ToInt();

			int hmRowsPerStrip = inHeightMap.IsTiled() ? 0 : inHeightMap.GetField(TiffTag.ROWSPERSTRIP)[0].ToInt();

			if (hmWidth <= 0 || hmHeight <= 0) {
				Util.Error("Invalid height map image size {0}x{1}", hmWidth, hmHeight);
			}

			{
				int hmChannelCount = inHeightMap.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
				SampleFormat hmSampleFormat = (SampleFormat)inHeightMap.GetField(TiffTag.SAMPLEFORMAT)[0].ToInt();

				if (hmChannelCount != 1 || hmSampleFormat != SampleFormat.IEEEFP) {
					Util.Error("Height map has {0} {1} channels. Should be exactly 1 UINT.",
						hmChannelCount, hmSampleFormat);
				}
			}

			VectorD3 hmScale = GeoKeyDir.GetModelPixelScale(inHeightMap);
			TiePoint[] hmTiePoints = GeoKeyDir.GetModelTiePoints(inHeightMap);
			GeoKeyDir hmGeoKeys = GeoKeyDir.GetGeoKeyDir(inHeightMap);

			float hmMinVal = (float)inHeightMap.GetField(TiffTag.SMINSAMPLEVALUE)[0].ToDouble();
			float hmMaxVal = (float)inHeightMap.GetField(TiffTag.SMAXSAMPLEVALUE)[0].ToDouble();
			float hmNoDataValue = (float)getGdalNoData(inHeightMap);
			float hmToRawTranslation = 0;
			float hmToRawScale = 0;

			uint hmOutWidth = 0;
			uint hmOutHeight = 0;
			uint hmOutBPP = 0;

			// HACK FOR NOW - Unity requires square textures?
			hmOutWidth = hmOutHeight = (uint)Math.Min(hmWidth, hmHeight);

			{
				var hmRasterF32 = new Raster<float>((uint)hmWidth, (uint)hmHeight);
				var hmRasterU16 = new Raster<ushort>();
				bool isByteSwapped = inHeightMap.IsByteSwapped();

				hmOutBPP = hmRasterU16.bitsPerPixel;

				if (hmRowsPerStrip > 0) {
					var tmpStrip = new byte[hmRasterF32.pitch * hmRowsPerStrip];

					for (int y = 0, s = 0; y < hmHeight; y += hmRowsPerStrip, s++) {
						int readByteCount = inHeightMap.ReadEncodedStrip(s, tmpStrip, 0, -1);
						int readRowCount = readByteCount / (int)hmRasterF32.pitch;

						if (	readRowCount == 0 || 
									readRowCount * hmRasterF32.pitch != readByteCount || 
									(readRowCount < hmRowsPerStrip && (y + hmRowsPerStrip < hmHeight)))
						{
							Util.Error("input height map corrupted.");
						}

						if (isByteSwapped) {
							Util.ByteSwap4(tmpStrip);
						}

						hmRasterF32.SetRawRows((uint)y, tmpStrip, (uint)readRowCount);
					}
				} else {
					int hmTileW = inHeightMap.GetField(TiffTag.TILEWIDTH)[0].ToInt();
					int hmTileH = inHeightMap.GetField(TiffTag.TILELENGTH)[0].ToInt();

					if ( hmTileW < hmWidth || hmTileH < hmHeight ) {
						Util.Error("Input heigh tmap is multi-tiled. Only stripped and single tile images supported.");
					}

					var tmpTile = new byte[hmRasterF32.sizeBytes];
					inHeightMap.ReadEncodedTile(0, tmpTile, 0, tmpTile.Length);
					if (isByteSwapped) {
						Util.ByteSwap4(tmpTile);
					}
					hmRasterF32.SetRawRows(0, tmpTile, (uint)hmHeight);
				}

				inHeightMap.Dispose();
				inHeightMap = null;

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
				hmRasterF32 = hmRasterF32.Clone(	hmRasterF32.width - hmOutWidth,
																					hmRasterF32.height - hmOutHeight,
																					hmOutWidth,
																					hmOutHeight );

				hmMinVal = float.MaxValue;
				hmMaxVal = float.MinValue;
				for (int i = 0; i < hmRasterF32.pixels.Length; i++) {
					var p = hmRasterF32.pixels[i];
					hmMinVal = Math.Min(hmMinVal, p);
					hmMaxVal = Math.Max(hmMaxVal, p);
				}

				hmToRawTranslation = (float)-hmMinVal;
				hmToRawScale = (float)((Math.Pow(2.0, hmRasterU16.bitsPerPixel) - 1) / (hmMaxVal + hmToRawTranslation));

				hmRasterF32.Convert(hmRasterU16, hmToRawTranslation, hmToRawScale);

				// Unity's default height map orientation is upside down WRT RGB textures.
				hmRasterU16.YFlip();

				using (var outFile = new FileStream(outputRawHeightPath, FileMode.Create, FileAccess.Write)) {
					outFile.Write(hmRasterU16.ToByteArray(), 0, (int)hmRasterU16.sizeBytes);
				}
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
				Util.Log("  Geo proj size: {0}x{1} {2}", projW, projH, hmGeoKeys.projLinearUnit);
				Util.Log("  Geo vertical range: {0} ({1}->{2}) {3}", projMaxV - projMinV, projMinV, projMaxV, hmGeoKeys.verticalLinearUnit);
				Util.Log("");
			}

			Tiff inRGB = Tiff.Open(inputRGBTifPath, "r");
			using (var stdout = Console.OpenStandardOutput()) {
				inRGB.PrintDirectory(stdout, TiffPrintFlags.NONE);
			}

			int rgbWidth = inRGB.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
			int rgbHeight = inRGB.GetField(TiffTag.IMAGELENGTH)[0].ToInt();

			{
				int rgbChannelCount = inRGB.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
				SampleFormat rgbSampleFormat = SampleFormat.UINT;

				var sampleFormatField = inRGB.GetField(TiffTag.SAMPLEFORMAT);
				if ( sampleFormatField != null) {
					rgbSampleFormat = (SampleFormat)sampleFormatField[0].ToInt();
				}

				if (rgbChannelCount != 3 || rgbSampleFormat != SampleFormat.UINT) {
					Util.Error("{0} has {1} {2} channels. Only 3 UINT supported.", inputRGBTifPath, rgbChannelCount, rgbSampleFormat);
				}
			}

			VectorD3 rgbScale = GeoKeyDir.GetModelPixelScale(inRGB);
			TiePoint[] rgbTiePoints = GeoKeyDir.GetModelTiePoints(inRGB);
			GeoKeyDir rgbGeoKeys = GeoKeyDir.GetGeoKeyDir(inRGB);
			int rgbRowsPerStrip = inRGB.IsTiled() ? 0 : inRGB.GetField(TiffTag.ROWSPERSTRIP)[0].ToInt();

			if (rgbWidth <= 0 || rgbHeight <= 0) {
				Util.Error("Invalid rgb image size {0}x{1}", rgbWidth, rgbHeight);
			}

			if (rgbWidth / hmWidth != rgbHeight / hmHeight) {
				Util.Error("Assymetric scaling of RGB image WRT height map.");
			}

			uint hmToRGBScale = (uint)(rgbWidth/hmWidth);
			uint rgbOutWidth = hmOutWidth * hmToRGBScale;
			uint rgbOutHeight = hmOutHeight * hmToRGBScale;
			uint rgbOutBPP = 24;
			var rgbOutCompression = Compression.LZW;

			{
				var rgbRaster = new Raster<ColorU8>((uint)rgbWidth, (uint)rgbHeight);

				if (rgbRowsPerStrip > 0) {
					var tmpStrip = new byte[rgbRaster.pitch * rgbRowsPerStrip];

					for (uint y = 0, s = 0 ; y < rgbHeight; y += (uint)rgbRowsPerStrip, s++) {
						int readByteCount = inRGB.ReadEncodedStrip((int)s, tmpStrip, 0, -1);
						int readRowCount = (int)(readByteCount / rgbRaster.pitch);

						if (readRowCount == 0 ||
								readRowCount * rgbRaster.pitch != readByteCount ||
								(readRowCount < rgbRowsPerStrip && (y + rgbRowsPerStrip < rgbHeight))) {
							Util.Error("input height map corrupted.");
						}

						rgbRaster.SetRawRows(y, tmpStrip, (uint)readRowCount);
					}
				} else {
					int rgbTileW = inRGB.GetField(TiffTag.TILEWIDTH)[0].ToInt();
					int rgbTileH = inRGB.GetField(TiffTag.TILELENGTH)[0].ToInt();

					if (rgbTileW < rgbWidth || rgbTileH < rgbHeight) {
						Util.Error("Input rgb image is multi-tiled. Only stripped and single tile images supported.");
					}

					var tmpTile = new byte[rgbRaster.sizeBytes];
					inRGB.ReadEncodedTile(0, tmpTile, 0, tmpTile.Length);
					rgbRaster.SetRawRows(0, tmpTile, rgbRaster.height);
				}

				inRGB.Dispose();
				inRGB = null;

				Util.Log( "Trimming RGB image to {0}x{1} to match height map.", rgbOutWidth, rgbOutHeight );

				rgbRaster = rgbRaster.Clone(	rgbRaster.width - rgbOutWidth,
																			rgbRaster.height - rgbOutHeight,
																			rgbOutWidth,
																			rgbOutHeight );

				// do the easy 2:1 reductions in integer space
				while( Math.Max(rgbRaster.width, rgbRaster.height) >= (8192 * 2)) {
					Util.Log("RGB image size {0}x{1} exceeds 8k max texture size by >= 2:1. Halving to {2}x{3}", 
						rgbRaster.width, rgbRaster.height, rgbRaster.width/2, rgbRaster.height/2);
					rgbRaster = rgbRaster.ScaledDown2to1();
				}

				// do the last fractional scaling down in float space
				if (Math.Max(rgbRaster.width, rgbRaster.height) > 8192) {
					double scaleDown = 8192.0 / Math.Max(rgbRaster.width, rgbRaster.height);
					uint scaleDownW = (uint)(scaleDown * rgbRaster.width);
					uint scaleDownH = (uint)(scaleDown * rgbRaster.height);
					Util.Log("RGB image size {0}x{1} exceeds 8k max texture size. Scaling to {0}x{1}", scaleDownW, scaleDownH);
					var rgbRasterF32 = rgbRaster.Convert(new Raster<ColorF32>()).Scaled(scaleDownW, scaleDownH);
					rgbRasterF32.Convert(rgbRaster);
				}

				rgbScale.x *= (double)rgbOutWidth/rgbRaster.width;
				rgbScale.y *= (double)rgbOutHeight/rgbRaster.height;
				rgbOutWidth = rgbRaster.width;
				rgbOutHeight = rgbRaster.height;

				using (Tiff outRGB = Tiff.Open(outputRGBTifPath, "w")) {
					outRGB.SetField(TiffTag.IMAGEWIDTH, (int)rgbRaster.width);
					outRGB.SetField(TiffTag.IMAGELENGTH, (int)rgbRaster.height);
					outRGB.SetField(TiffTag.BITSPERSAMPLE, 8);
					outRGB.SetField(TiffTag.SAMPLESPERPIXEL, 3);
					outRGB.SetField(TiffTag.SAMPLEFORMAT, SampleFormat.UINT);
					outRGB.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
					outRGB.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
					outRGB.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
					outRGB.SetField(TiffTag.COMPRESSION, rgbOutCompression);
					outRGB.SetField(TiffTag.TILEWIDTH, (int)rgbRaster.width);
					outRGB.SetField(TiffTag.TILELENGTH, (int)rgbRaster.height);

					outRGB.WriteEncodedTile(0, rgbRaster.ToByteArray(), (int)rgbRaster.sizeBytes);
				}
			}

			{
				float projW = (float)(rgbOutWidth * rgbScale.x);
				float projH = (float)(rgbOutHeight * rgbScale.y);

				Util.Log("");
				Util.Log("Output RGB Image: {0}", outputRGBTifPath);
				Util.Log("  Dimensions: {0}x{1} {2} bit pix", rgbOutWidth, rgbOutHeight, rgbOutBPP);
				Util.Log("  Compression: {0}", rgbOutCompression);
				Util.Log("  Geo proj size: {0}x{1} {2}", projW, projH, hmGeoKeys.projLinearUnit);
				Util.Log("");
			}
		}
	}
}
