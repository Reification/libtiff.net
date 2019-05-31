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
			int hmRowsPerStrip = inHeightMap.GetField(TiffTag.ROWSPERSTRIP)[0].ToInt();

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

			uint hmOutWidth = (uint)Math.Pow(2.0, Math.Floor(Math.Log(hmWidth - 1) / Math.Log(2.0))) + 1;
			uint hmOutHeight = (uint)Math.Pow(2.0, Math.Floor(Math.Log(hmHeight - 1) / Math.Log(2.0))) + 1;
			uint hmOutBPP = 0;

			{
				var hmRasterF32 = new Raster<float>((uint)hmWidth, (uint)hmHeight);
				var hmRasterU16 = new Raster<ushort>();

				hmOutBPP = hmRasterU16.bitsPerPixel;

				{
					var hmPitch = hmWidth * sizeof(float);
					var tmpStrip = new byte[hmPitch * hmRowsPerStrip];
					bool isByteSwapped = inHeightMap.IsByteSwapped();

					for (int y = 0, s = 0; y < hmHeight; y += hmRowsPerStrip, s++) {
						int readByteCount = inHeightMap.ReadEncodedStrip(s, tmpStrip, 0, -1);
						int readRowCount = readByteCount / hmPitch;

						if (	readRowCount == 0 || 
									readRowCount * hmPitch != readByteCount || 
									(readRowCount < hmRowsPerStrip && (y + hmRowsPerStrip < hmHeight)))
						{
							Util.Error("input height map corrupted.");
						}

						if (isByteSwapped) {
							Util.ByteSwap4(tmpStrip);
						}

						hmRasterF32.SetRawRows((uint)y, tmpStrip, (uint)readRowCount);
					}

					inHeightMap.Dispose();
					inHeightMap = null;
				}

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
				hmRasterF32 = hmRasterF32.Clone((uint)hmWidth - hmOutWidth, (uint)hmHeight - hmOutHeight, hmOutWidth, hmOutHeight);

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

				// Unity's default heightmap orientation is upside-down WRT RGB textures.
				hmRasterU16.YFlip();

				using (var outFile = new FileStream(outputRawHeightPath, FileMode.Create, FileAccess.Write)) {
					var rasterBytes = hmRasterU16.ToByteArray();
					outFile.Write(rasterBytes, 0, rasterBytes.Length);
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
				Util.Log("  Geo vertical range: {0} ({1}->{2})", projMaxV - projMinV, projMinV, projMaxV, hmGeoKeys.verticalLinearUnit);
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
			int rgbRowsPerStrip = inRGB.GetField(TiffTag.ROWSPERSTRIP)[0].ToInt();

			if (rgbWidth <= 0 || rgbHeight <= 0) {
				Util.Error("Invalid rgb image size {0}x{1}", rgbWidth, rgbHeight);
			}

			if (rgbWidth / hmWidth != rgbHeight / hmHeight) {
				Util.Error("Assymetric scaling of RGB image WRT height map.");
			}

			float hmToRGBScale = (float)rgbWidth/hmWidth;
			uint rgbOutWidth = (uint)((hmOutWidth - 1) * hmToRGBScale + 0.5f);
			uint rgbOutHeight = (uint)((hmOutHeight - 1) * hmToRGBScale + 0.5f);
			uint rgbOutBPP = 0;

			{
				// multiply width by 3 - we're using bytes instead of RGB struct
				var rgbRaster = new Raster<byte>((uint)rgbWidth * 3, (uint)rgbHeight);

				rgbOutBPP = rgbRaster.bitsPerPixel * 3;

				{
					int rgbPitch = rgbWidth * 3;
					var tmpStrip = new byte[rgbPitch * rgbRowsPerStrip];

					for (uint y = 0, s = 0 ; y < rgbHeight; y += (uint)rgbRowsPerStrip, s++) {
						int readByteCount = inRGB.ReadEncodedStrip((int)s, tmpStrip, 0, -1);
						int readRowCount = readByteCount / rgbPitch;

						if (readRowCount == 0 ||
								readRowCount * rgbPitch != readByteCount ||
								(readRowCount < rgbRowsPerStrip && (y + rgbRowsPerStrip < rgbHeight))) {
							Util.Error("input height map corrupted.");
						}

						rgbRaster.SetRows(y, tmpStrip, (uint)readRowCount);
					}

					inRGB.Dispose();
					inRGB = null;
				}

				// crop to bottom right (for now) - the * 3 is because we're using a byte raster, not an RGB struct raster.
				rgbRaster = rgbRaster.Clone(	(uint)(rgbWidth - rgbOutWidth) * 3,
																			(uint)rgbHeight - rgbOutHeight,
																			rgbOutWidth * 3,
																			rgbOutHeight );

				using (Tiff outRGB = Tiff.Open(outputRGBTifPath, "w")) {
					const Compression comprLossless = Compression.LZW;
					const Compression comprLossy = Compression.JP2000;

					Compression rgbOutCompression = Util.IsJpg(outputRGBTifPath) ? comprLossy : comprLossless;
					int rgbOutRowsPerStrip = (rgbOutCompression == comprLossy) ? 1024 : 32;

					outRGB.SetField(TiffTag.IMAGEWIDTH, rgbOutWidth);
					outRGB.SetField(TiffTag.IMAGELENGTH, rgbOutHeight);
					outRGB.SetField(TiffTag.BITSPERSAMPLE, 8);
					outRGB.SetField(TiffTag.SAMPLESPERPIXEL, 3);
					outRGB.SetField(TiffTag.SAMPLEFORMAT, SampleFormat.UINT);
					outRGB.SetField(TiffTag.ROWSPERSTRIP, rgbOutRowsPerStrip);
					outRGB.SetField(TiffTag.COMPRESSION, rgbOutCompression);
					outRGB.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
					outRGB.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
					outRGB.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);

					uint rgbOutPitch = rgbOutWidth * 3;
					var tmpStrip = new byte[rgbOutPitch * rgbOutRowsPerStrip];
					for (uint y = 0, s = 0; y < rgbOutHeight; y += (uint)rgbOutRowsPerStrip, s++) {
						uint rowsLeft = Math.Min((uint)rgbOutRowsPerStrip, (uint)rgbOutHeight - y);
						rgbRaster.GetRows(y, tmpStrip, rowsLeft);
						outRGB.WriteEncodedStrip((int)y, tmpStrip, (int)(rowsLeft * rgbOutPitch));
					}
				}
			}

			{
				float projW = (float)(rgbOutWidth * rgbScale.x);
				float projH = (float)(rgbOutHeight * rgbScale.y);

				Util.Log("");
				Util.Log("Output RGB Image: {0}", outputRGBTifPath);
				Util.Log("  Dimensions: {0}x{1} pix {2} bpp", rgbOutWidth, rgbOutHeight, rgbOutBPP);
				Util.Log("  Geo proj size: {0}x{1} {2}", projW, projH, hmGeoKeys.projLinearUnit);
				Util.Log("");
			}
		}
	}
}
