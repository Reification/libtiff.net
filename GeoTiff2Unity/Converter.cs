// normally only raw format height tiles are saved.
// enable to save height tiles in tiff format as well.
// useful for debugging height tile output. unity does not handle tiff for import.
//#define SAVE_TIFF_HEIGHT_TILES
// when enabled areas of source raster are cleared with white bordered black rectangles as they are covered.
// uncovered areas are cleared to gray. the modified height & rgb maps are saved as tiffs in .HeightMaps/
// along with the height map tiles.
//#define DEBUG_RECURSION_COVERAGE

using System;
using System.IO;
using System.Collections.Generic;
using BitMiracle.LibTiff.Classic;
using System.Runtime.InteropServices;

namespace GeoTiff2Unity {
	using GTHeightRaster = Raster<float>;
	using HeightRaster = Raster<ushort>;
	using ColorRaster = Raster<ColorU8>;

	public class Converter {
		public const uint kMaxUnityTexSize = 8 * 1024;
		public const uint kBCBlockSize = 4;

		public const uint kMinMaxRGBTexSize = 512;
		public const uint kMaxRGBTexSize = kMaxUnityTexSize;
		public const bool kDefaultRGBScaleToEvenBCBlockSize = true;

		public const uint kMinHeightTexSize = 33;
		public const uint kMaxHeightTexSize = (4 * 1024) + 1;

		public RasterRotation preRotation = RasterRotation.None;

		public string hmTiffInPath = null;
		public string rgbTiffInPath = null;
		public string outPathBase = null;

		public uint hmOutMaxTexSize = kMaxHeightTexSize;
		public uint hmOutMinTexSize = kMinHeightTexSize;
		public uint rgbOutMaxTexSize = kMaxRGBTexSize;
		public bool rgbScaleToEvenBCBlockSize = kDefaultRGBScaleToEvenBCBlockSize;

		public static bool IsValidHeightMapSize(uint sz) {
			uint potCheck = sz - 1;
			return (potCheck & (potCheck - 1)) == 0u;
		}

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

		void go() {
			loadHeightMapHeader();

			loadRGBHeader();

			computeAlignment();

			computePrimaryTileSizes();

			processHeightMap();

			processRGBImage();

			if (regionIdSet.Count != 0) {
				Util.Error( "{0} height tiles are missing corresponding RGB images!", regionIdSet.Count );
			}
		}

		void loadHeightMapHeader() {
			hmTiffIn = loadGeoTiffHeader(hmHeader, hmTiffInPath);

			if (hmHeader.channelCount != 1 || hmHeader.sampleFormat != SampleFormat.IEEEFP || hmHeader.bitsPerChannel != 32) {
				Util.Error("Height map {0} has {1} {2} bit {3} channels. Expected exactly 1 32 bit IEEEFP.",
					hmTiffInPath, hmHeader.channelCount, hmHeader.bitsPerChannel, hmHeader.sampleFormat);
			}

			double offSquareness = Math.Abs((hmHeader.pixToProjScale.x - hmHeader.pixToProjScale.y) / ((VectorD2)hmHeader.pixToProjScale).Max());
			if (offSquareness >= 0.01) {
				Util.Error("Height map {0} pix->projection scaling {1} is off square by {2}%. Aspect scaling not implemented.", 
					hmTiffInPath, (VectorD2)hmHeader.pixToProjScale, (offSquareness * 100));
				/// TODO: if this becomes a thing we'll need to scale the height map after loading it to correct.
			}
		}

		void loadRGBHeader() {
			rgbTiffIn = loadGeoTiffHeader(rgbHeader, rgbTiffInPath);

			if (rgbHeader.channelCount != 3 || rgbHeader.sampleFormat != SampleFormat.UINT || rgbHeader.bitsPerChannel != 8) {
				Util.Error("RGB image {0} has {1} {2} bit {3} channels. Expected 3 8 bit UINT.",
					rgbTiffInPath, rgbHeader.channelCount, rgbHeader.bitsPerChannel, rgbHeader.sampleFormat);
			}

			double offSquareness = Math.Abs((rgbHeader.pixToProjScale.x - rgbHeader.pixToProjScale.y) / ((VectorD2)rgbHeader.pixToProjScale).Max());
			if (offSquareness >= 0.01) {
				Util.Error("RGB image {0} pix->projection scaling {1} is off square by {2}%. Aspect scaling not implemented.",
					rgbTiffInPath, (VectorD2)rgbHeader.pixToProjScale, (offSquareness * 100));
				/// TODO: if this becomes a thing we'll need to scale the rgb image after loading it to correct.
			}
		}

		void computeAlignment() {
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
			Util.Log("  Proj Point Delta: {0} meters", tiePointDelta);
			Util.Log("  Proj Point HM Pix Delta: {0}", tiePointDelta / (VectorD2)hmHeader.pixToProjScale);
			Util.Log("  Proj Point RGB Pix Delta: {0}", tiePointDelta / (VectorD2)rgbHeader.pixToProjScale);

			if (hmHeader.geoKeys.projLinearUnit != hmHeader.geoKeys.verticalLinearUnit) {
				Util.Error("Mismatch between height map plane units {0} and height map vertical units {1}",
					hmHeader.geoKeys.projLinearUnit, hmHeader.geoKeys.verticalLinearUnit);
			}

			if (hmHeader.geoKeys.projLinearUnit != rgbHeader.geoKeys.projLinearUnit) {
				Util.Error("Mismatch between height map units {0} and rgb image units {1}",
					hmHeader.geoKeys.projLinearUnit, rgbHeader.geoKeys.projLinearUnit);
			}

			{
				double tpMisalign = (tiePointDelta.Abs() / (VectorD2)hmHeader.pixToProjScale).Max();

				// misalignment ~1/2 height map pixel is accounted for by diff in raster types - one is pixIsArea the other pixIsPoint.
				// see raster space doc: http://geotiff.maptools.org/spec/geotiff2.5.html

				if (tpMisalign >= 2.0) {
					Util.Error("  Tie Points excessively misaligned by {0} height map pixels ({1} {2})!",
						tpMisalign, tpMisalign * hmHeader.pixToProjScale.Max(), hmHeader.geoKeys.projLinearUnit);
				} else if (tpMisalign >= 1.0) {
					Util.Warn("  Tie Points misaligned by {0} height map pixels ({1} {2})!",
						tpMisalign, tpMisalign * hmHeader.pixToProjScale.Max(), hmHeader.geoKeys.projLinearUnit);
				}
			}

			/// HACK!
			rgbHeader.pixToProjScale = (VectorD2)((VectorD2)(rgbHeader.pixToProjScale)).Max();

			// we're going with the notion that the height and rgb maps line up.
			//hmToRGBPixScale = hmHeader.pixToProjScale / rgbHeader.pixToProjScale;
			hmToRGBPixScale = rgbHeader.sizePix / hmHeader.sizePix;
		}

		void computePrimaryTileSizes() {
			hmOutTileSizePix = (VectorD2)Math.Min(calcHeightMapSizeLTE(hmHeader.sizePix.Min()), hmOutMaxTexSize);

			VectorD2 rgbOutTileSizePix = hmOutTileSizePix * hmToRGBPixScale;

			while (rgbOutTileSizePix.Max() > rgbOutMaxTexSize) {
				hmOutTileSizePix = (VectorD2)calcHeightMapSizeLTE(hmOutTileSizePix.x - 1);
				rgbOutTileSizePix = (hmOutTileSizePix * hmToRGBPixScale).Floor();
			}
		}

		void processHeightMap() {
			HeightRaster hmRasterOut = null;

			{
				var hmRasterIn = new GTHeightRaster();

				Util.Log("");
				loadPixelData(ref hmTiffIn, hmHeader, hmRasterIn);

				hmRasterIn = processHeightMapData(hmRasterIn);

				if (preRotation != RasterRotation.None) {

					hmRasterIn.Rotate(preRotation);
					hmHeader.Rotate(preRotation);
				}

				float gtToRawTrans = (float)-hmHeader.minSampleValue;
				float gtToRawScale = HeightRaster.maxChannelValue/(float)(hmHeader.maxSampleValue - hmHeader.minSampleValue);
				hmRasterOut = hmRasterIn.Convert(new HeightRaster(), gtToRawTrans, gtToRawScale);
			}

			// Unity height map textures are (by default) bottom to top.
			switch (hmHeader.orientation) {
			case Orientation.TOPLEFT:
				hmOutYFlipNeeded = true;
				break;
			case Orientation.BOTLEFT:
				break;
			default:
				Util.Error("Unsupported height map orientation {0}", hmHeader.orientation);
				break;
			}

			Util.Log("");
			generateTiles(0, (VectorD2)0, hmRasterOut, null, (VectorD2)0, hmHeader.sizePix, hmOutTileSizePix);

#if DEBUG_RECURSION_COVERAGE
		writeRGBTiffOut(Path.GetDirectoryName(outPathBase) + "/" + hmRawOutSubDirName + "/hmCoverage.tif", hmRasterOut);
#endif
		}

		void processRGBImage() {
			var rgbRasterIn = new ColorRaster();

			Util.Log("");
			loadPixelData(ref rgbTiffIn, rgbHeader, rgbRasterIn);

			if (preRotation != RasterRotation.None) {

				rgbRasterIn.Rotate(preRotation);

				if (hmHeader.sizePix != rgbRasterIn.GetSizePix()) {
					hmToRGBPixScale = hmToRGBPixScale.yx;
				}

				rgbHeader.Rotate(preRotation);
			}

			Util.Log("");
			generateTiles(0, (VectorD2)0, null, rgbRasterIn, (VectorD2)0, hmHeader.sizePix, hmOutTileSizePix);

#if DEBUG_RECURSION_COVERAGE
			writeRGBTiffOut(Path.GetDirectoryName(outPathBase) + "/" + hmRawOutSubDirName + "/rgbCoverage.tif", rgbRasterIn);
#endif
		}

		void generateTiles(uint recursionDepth, VectorD2 regionId, HeightRaster hmRasterOut, ColorRaster rgbRasterOut, VectorD2 hmRgnOrigin, VectorD2 hmRgnSize, VectorD2 hmRgnTileSize) {
			if (hmRasterOut != null) {
				if (regionIdSet.Contains(regionId)) {
					Util.Error("[{0}{1}] Height tile pass: regionId collision!", recursionDepth, regionId);
				}

				regionIdSet.Add(regionId);
			} else if (rgbRasterOut != null) {
				if (!regionIdSet.Contains(regionId)) {
					Util.Error("[{0}{1}] RGB tile pass: regionId collision or variance from height tile pass!", recursionDepth, regionId);
				}

				regionIdSet.Remove(regionId);
			}

			if (hmRgnTileSize.Min() < hmOutMinTexSize) {
				Util.Log("\n[{0}-{1}] Skipping region size {2}, height tile size {3} below min height tile size {4}",
					recursionDepth, regionId, hmRgnSize, hmRgnTileSize, hmOutMinTexSize);

#if DEBUG_RECURSION_COVERAGE
				if (hmRasterOut != null) {
					ushort gray = (ushort.MaxValue >> 1);
					hmRasterOut.Clear(gray, hmRgnOrigin, hmRgnSize);
				}

				if (rgbRasterOut != null) {
					ColorU8 gray = ColorU8.white;
					gray.r >>= 1; gray.g >>= 1; gray.b >>= 1;

					rgbRasterOut.Clear(gray, (hmRgnOrigin * hmToRGBPixScale).Floor(), (hmRgnSize * hmToRGBPixScale).Floor());
				}
#endif

				return;
			}

			VectorD2 rgbRgnTileSize = (hmRgnTileSize * hmToRGBPixScale).Floor();
			VectorD2 bcRgnTileSize = (rgbRgnTileSize / kBCBlockSize).Ceiling() * kBCBlockSize;
			bool rgbTileScaleNeeded = (rgbRasterOut != null && rgbScaleToEvenBCBlockSize && (rgbRgnTileSize != bcRgnTileSize));

			HeightRaster hmTileRaster = new HeightRaster();
			ColorRaster rgbTileRaster = new ColorRaster();

			VectorD2 hmRgnGridSize = (hmRgnSize / hmRgnTileSize).Floor();
			VectorD2 hmCoveredRgnSize = (hmRgnGridSize * hmRgnTileSize);

			if ((hmRgnSize - hmCoveredRgnSize).Min() < 0) {
				Util.Error("internal error: coverage exceeds region!");
			}

			if (hmRasterOut != null) {
				Util.Log("\n[{0}-{1}] Writing {2} grid of {3} sized {4} height tiles for region sized {5}", 
					recursionDepth, regionId, hmRgnGridSize, hmRgnTileSize, HeightRaster.pixelTypeName, hmRgnSize);
				hmTileRaster.Init(hmRgnTileSize);
			}

			if (rgbRasterOut != null) {
				Util.Log("\n[{0}-{1}] Writing {2} grid of {3} sized RGB tiles matching {4} sized height tiles for region sized {5}",
					recursionDepth, regionId, hmRgnGridSize, rgbRgnTileSize, hmRgnTileSize, hmRgnSize);
				rgbTileRaster.Init(rgbRgnTileSize);
			}

			if (rgbTileScaleNeeded) {
				Util.Log("  [{0}-{1}] RGB tiles will be scaled to {2}, multiple of {3} for compatibility with block compression algorithms.",
					recursionDepth,
					regionId,
					bcRgnTileSize,
					kBCBlockSize);
			}

			VectorD2 hmTileCoords = 0;
			VectorD2 hmTileOrigin = hmRgnOrigin;
			VectorD2 hmRgnEnd = hmRgnOrigin + hmRgnSize;
			VectorD2 hmCoveredRgnEnd = hmRgnOrigin + hmCoveredRgnSize;

			for (; hmTileOrigin.y < hmCoveredRgnEnd.y; hmTileOrigin.y += hmRgnTileSize.y) {

				for (hmTileOrigin.x = hmRgnOrigin.x; hmTileOrigin.x < hmCoveredRgnEnd.x; hmTileOrigin.x += hmRgnTileSize.x) {
					if (hmRasterOut != null) {
						hmRasterOut.GetRect(hmTileRaster, hmTileOrigin);

#if DEBUG_RECURSION_COVERAGE
						hmRasterOut.Clear(ushort.MaxValue, hmTileOrigin, hmRgnTileSize);
						hmRasterOut.Clear(ushort.MinValue, hmTileOrigin + (VectorD2)2, hmRgnTileSize - (VectorD2)4);
#endif

						string hmTileOutPath = genHMRawOutPath(regionId, hmTileCoords, hmTileRaster);
#if SAVE_TIFF_HEIGHT_TILES
						writeRGBTiffOut(hmTileOutPath.Replace(".r9nh", ".tif"), hmTileRaster);
#endif
						if (hmOutYFlipNeeded) {
							hmTileRaster.YFlip();
						}

						writeRawTileOut(hmTileOutPath, hmTileRaster);
						Util.Log("  [{0}-{1}] wrote height tile {2}", recursionDepth, regionId, Path.GetFileName(hmTileOutPath));
					}

					if (rgbRasterOut != null) {
						var rgbTileOrigin = (hmTileOrigin * hmToRGBPixScale).Floor();
						rgbRasterOut.GetRect(rgbTileRaster, rgbTileOrigin);

#if DEBUG_RECURSION_COVERAGE
						rgbRasterOut.Clear(ColorU8.white, rgbTileOrigin, rgbRgnTileSize);
						rgbRasterOut.Clear(ColorU8.black, rgbTileOrigin + (VectorD2)2, rgbRgnTileSize - (VectorD2)4);
#endif

						string rgbTileOutPath = genRGBTiffOutPath(regionId, hmTileCoords, rgbTileRaster);

						if (rgbTileScaleNeeded) {
							writeRGBTiffOut(rgbTileOutPath, rgbTileRaster.Scaled(bcRgnTileSize), rgbHeader.orientation);
						} else {
							writeRGBTiffOut(rgbTileOutPath, rgbTileRaster, rgbHeader.orientation);
						}

						Util.Log("  [{0}-{1}] wrote rgb tile {2}", recursionDepth, regionId, Path.GetFileName(rgbTileOutPath));
					}

					hmTileCoords.x++;
				}

				hmTileCoords.x = 0;
				hmTileCoords.y++;
			}

			VectorD2 hmEdgeSizes = hmRgnSize - hmCoveredRgnSize;

			if (hmEdgeSizes == 0) {
				Util.Log("  [{0}-{1}] Region finished with an exact fit.", recursionDepth, regionId);
				return;
			}

			{
				// whichever edge is wider gets the corner.
				bool cornerGoesToRightEdge = (hmEdgeSizes.x > hmEdgeSizes.y);

				VectorD2 rightEdgeSize;
				VectorD2 bottomEdgeSize;

				if (cornerGoesToRightEdge) {
					rightEdgeSize = (hmEdgeSizes * VectorD2.v10) + (hmRgnSize * VectorD2.v01);
					bottomEdgeSize = ((hmRgnSize - hmEdgeSizes) * VectorD2.v10) + (hmEdgeSizes * VectorD2.v01);
				} else {
					rightEdgeSize = (hmEdgeSizes * VectorD2.v10) + ((hmRgnSize - hmEdgeSizes) * VectorD2.v01);
					bottomEdgeSize = (hmRgnSize * VectorD2.v10) + (hmEdgeSizes * VectorD2.v01);
				}

				Util.Log("  [{0}-{1}] Region finishged with pending edge region sizes right: {2}{3} bottom: {4}{5}", 
					recursionDepth, regionId, 
					rightEdgeSize, cornerGoesToRightEdge ? "(incl. corner)" : "",
					bottomEdgeSize, !cornerGoesToRightEdge ? "(incl. corner)" : "" );

				uint idStepSize = (1u << (int)recursionDepth);

				// recurse into right edge
				generateTiles(	recursionDepth + 1,
												regionId + (idStepSize * VectorD2.v10),
												hmRasterOut, rgbRasterOut,
												hmRgnOrigin + (hmCoveredRgnSize * VectorD2.v10), 
												rightEdgeSize,
												calcHeightMapSizeLTE(rightEdgeSize.Min()) );

				// recurse into bottom edge
				generateTiles(	recursionDepth + 1,
												regionId + (idStepSize * VectorD2.v01),
												hmRasterOut, rgbRasterOut,
												hmRgnOrigin + (hmCoveredRgnSize * VectorD2.v01),
												bottomEdgeSize, 
												calcHeightMapSizeLTE(bottomEdgeSize.Min()) );
			}
		}

		static double calcHeightMapSizeLTE(double curSize) {
			if (curSize <= 2) {
				return 2;
			}
			// Unity height maps must be size 2^N + 1 
			var lteSize = Math.Pow(2.0, Math.Floor(Math.Log(curSize - 1) / Math.Log(2.0))) + 1.0;
			if (IsValidHeightMapSize((uint)curSize) && (lteSize != curSize)) {
				Util.Error("internal error: hmSize {0} was valid, result should not be {1}", curSize, lteSize);
			}
			return lteSize;
		}

		GTHeightRaster processHeightMapData(GTHeightRaster hmRaster) {
			int noDataCount = 0;

			if (hmHeader.minSampleValue == 0 && hmHeader.maxSampleValue == 0) {
				var minV = hmRaster.pixels[0];
				var maxV = minV;

				for (int i = 1; i < hmRaster.pixels.Length; i++) {
					var p = hmRaster.pixels[i];
					minV = Math.Min(p, minV);
					maxV = Math.Max(p, maxV);
				}

				hmHeader.minSampleValue = minV;
				hmHeader.maxSampleValue = maxV;
			}

			var noDataValue = (float)hmHeader.noSampleValue;

			for (int i = 0; i < hmRaster.pixels.Length; i++) {
				var p = hmRaster.pixels[i];

				if (p == noDataValue) {
					noDataCount++;
					hmRaster.pixels[i] = (float)hmHeader.minSampleValue;
				}
			}

			if (noDataCount > 0) {
				Util.Warn("  Replaced {0} no-data pixels with minValue {1}", noDataCount, (float)hmHeader.minSampleValue);
				Util.Warn("  If this is more than infrequent we will need a masking strategy.");
			}

			return hmRaster;
		}

		ColorRaster processRGBData(ColorRaster rgbRaster) {
			return rgbRaster;
		}

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

			var minValField = tiff.GetField(TiffTag.MINSAMPLEVALUE);
			var maxValField = tiff.GetField(TiffTag.MAXSAMPLEVALUE);
			hdr.minSampleValue = minValField != null ? (float)minValField[0].ToDouble() : 0;
			hdr.maxSampleValue = maxValField != null ? (float)maxValField[0].ToDouble() : 0;
			hdr.noSampleValue = getGDALNoSampleValue(tiff);

			Util.Log("  Pix to geo proj scale: {0} {1}", hdr.pixToProjScale, hdr.geoKeys.projLinearUnit);

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

					if (readRowCount * raster.pitch != readByteCount ||
							 (readRowCount < hdr.rowsPerStrip && (y + hdr.rowsPerStrip < h))) {
						Util.Error("tiff image {0} corrupted.", tiff.FileName());
					}

					if (shouldByteSwapStrip) {
						Util.ByteSwap4(tmpStrip);
					}

					raster.SetRawRows((uint)y, tmpStrip, (uint)readRowCount);
				}
			} else {
				Util.Error("{0} is tiled, not striped. tiled reading not yet implemented.", tiff.FileName());
				/// @TODO: implement tiled reading.
				/// Raster<T>.SetRawRect() is implemented, so tiled reading should be pretty straightforward.
			}

			tiff.Dispose();
			tiff = null;
		}

		void writeRawTileOut<T>(string path, Raster<T> raster) where T : struct {
			const uint kRowsPerStrip = 32;

			using (var outRaw = new FileStream(path, FileMode.Create, FileAccess.Write)) {
				HeightTileHeader hdr = new HeightTileHeader();
				hdr.Init<T>(raster.width, (float)hmHeader.pixToProjScale.Max(), (float)hmHeader.minSampleValue, (float)hmHeader.maxSampleValue);
				hdr.Write(outRaw);

				byte[] tmpStrip = null;

				for (uint y = 0, si = 0; y < raster.height; y += kRowsPerStrip, si++) {
					uint stripRowCount = Math.Min(kRowsPerStrip, raster.height - y);

					raster.GetRawRows(y, ref tmpStrip, stripRowCount);

					outRaw.Write(tmpStrip, 0, (int)(stripRowCount * raster.pitch));
				}
			}
		}

		void writeRGBTiffOut<T>(string path, Raster<T> raster, Orientation orientation = Orientation.TOPLEFT) where T : struct {
			const uint kRowsPerStrip = 32;

			using (Tiff outTif = Tiff.Open(path, "w")) {
				outTif.SetField(TiffTag.IMAGEWIDTH, (int)raster.width);
				outTif.SetField(TiffTag.IMAGELENGTH, (int)raster.height);
				outTif.SetField(TiffTag.SAMPLESPERPIXEL, (int)Raster<T>.channelCount);
				outTif.SetField(TiffTag.BITSPERSAMPLE, (int)Raster<T>.bitsPerChannel);
				outTif.SetField(TiffTag.SAMPLEFORMAT, Raster<T>.channelTypeName.StartsWith("float") ? SampleFormat.IEEEFP : SampleFormat.UINT);
				outTif.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
				outTif.SetField(TiffTag.ORIENTATION, orientation);
				outTif.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
				outTif.SetField(TiffTag.COMPRESSION, Compression.LZW);
				outTif.SetField(TiffTag.PREDICTOR, Predictor.HORIZONTAL);
				outTif.SetField(TiffTag.ROWSPERSTRIP, (int)kRowsPerStrip);

				byte[] tmpStrip = null;

				for (uint y = 0, si = 0; y < raster.height; y += kRowsPerStrip, si++) {
					uint stripRowCount = Math.Min(kRowsPerStrip, raster.height - y);

					raster.GetRawRows(y, ref tmpStrip, stripRowCount);

					outTif.WriteEncodedStrip((int)si, tmpStrip, (int)(stripRowCount * raster.pitch));
				}
			}
		}

		static double[] getModelTransformation(Tiff tif) {
			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.MODELTRANSFORMATIONTAG);
			if (v?.Length == 2 && v[0].ToInt() == 16) {
				var val = v[1].ToDoubleArray();
				return val;
			}
			return null;
		}

		static int getGDALNoSampleValue(Tiff tif) {
			FieldValue[] v = tif.GetField((TiffTag)(int)GeoTiffTag.GDALNODATATAG);
			int val = int.MinValue;
			if (v?.Length > 1) {
				int.TryParse(v[1].ToString(), out val);
			}
			return val;
		}

		static readonly string hmRawOutPathFmt = "{0}/{1}_HM_{2:D2}-{3:D2}_{4:D3}-{5:D3}.r9nh";
		static readonly string rgbOutPathFmt = "{0}/{1}_RGB_{2:D2}-{3:D2}_{4:D3}-{5:D3}.tif";
		static readonly string hmRawOutSubDirName = ".HeightMaps";

		string genHMRawOutPath(VectorD2 tileRegion, VectorD2 tilePos, HeightRaster hmRaster) {
			string outDir = Path.GetDirectoryName(outPathBase) + "/" + hmRawOutSubDirName;
			string outNameBase = Path.GetFileName(outPathBase);
			bool isFloat = (hmRaster.getChannelType() == typeof(float));

			if (!Directory.Exists(outDir) ) {
				Directory.CreateDirectory(outDir);
			}

			string path = string.Format(	hmRawOutPathFmt,
																		outDir, outNameBase,
																		(uint)tileRegion.y,
																		(uint)tileRegion.x,
																		(uint)tilePos.y, 
																		(uint)tilePos.x);
			return path;
		}

		string genRGBTiffOutPath(VectorD2 tileRegion, VectorD2 tilePos, ColorRaster rgbRaster) {
			string outDir = Path.GetDirectoryName(outPathBase);
			string outNameBase = Path.GetFileName(outPathBase);
			string path = string.Format(rgbOutPathFmt,
																		outDir, outNameBase,
																		(uint)tileRegion.y,
																		(uint)tileRegion.x,
																		(uint)tilePos.y,
																		(uint)tilePos.x);
			return path;
		}

		Tiff hmTiffIn = null;
		GeoTiffHeader hmHeader = new GeoTiffHeader();
		bool hmOutYFlipNeeded = false;

		Tiff rgbTiffIn = null;
		GeoTiffHeader rgbHeader = new GeoTiffHeader();

		VectorD2 hmToRGBPixScale = (VectorD2)0;
		VectorD2 hmOutTileSizePix = (VectorD2)0;
		HashSet<VectorD2> regionIdSet = new HashSet<VectorD2>();
	}

	public static class RasterExt {
		public static VectorD2 GetSizePix<T>(this Raster<T> r) where T : struct {
			return new VectorD2 { x = r.width, y = r.height };
		}

		public static void Init<T>(this Raster<T> r, VectorD2 sizePix) where T : struct {
			r.Init((uint)sizePix.width, (uint)sizePix.height);
		}

		public static Raster<T> Clone<T>(this Raster<T> r, VectorD2 origin, VectorD2 sizePix) where T : struct {
			return r.Clone((uint)origin.x, (uint)origin.y, (uint)sizePix.width, (uint)sizePix.height);
		}

		public static void Clear<T>(this Raster<T> r, T clearVal, VectorD2 origin, VectorD2 sizePix) where T : struct {
			r.Clear(clearVal, (uint)origin.x, (uint)origin.y, (uint)sizePix.width, (uint)sizePix.height);
		}

		public static void GetRect<T>(this Raster<T> r, ref T[] rect, VectorD2 origin, VectorD2 sizePix) where T : struct {
			r.GetRect(ref rect, (uint)origin.x, (uint)origin.y, (uint)sizePix.width, (uint)sizePix.height);
		}

		public static void SetRect<T>(this Raster<T> r, T[] rect, VectorD2 origin, VectorD2 sizePix) where T : struct {
			r.SetRect(rect, (uint)origin.x, (uint)origin.y, (uint)sizePix.width, (uint)sizePix.height);
		}

		public static void GetRect<T>(this Raster<T> r, Raster<T> rect, VectorD2 origin) where T : struct {
			r.GetRect(rect, (uint)origin.x, (uint)origin.y);
		}

		public static void SetRect<T>(this Raster<T> r, Raster<T> rect, VectorD2 origin) where T : struct {
			r.SetRect(rect, (uint)origin.x, (uint)origin.y);
		}

		public static byte[] CloneRaw<T>(this Raster<T> r, VectorD2 origin, VectorD2 sizePix) where T : struct {
			return r.CloneRaw((uint)origin.x, (uint)origin.y, (uint)sizePix.width, (uint)sizePix.height);
		}

		public static void GetRawRect<T>(this Raster<T> r, ref byte[] rect, VectorD2 origin, VectorD2 sizePix) where T : struct {
			r.GetRawRect(ref rect, (uint)origin.x, (uint)origin.y, (uint)sizePix.width, (uint)sizePix.height);
		}

		public static void SetRawRect<T>(this Raster<T> r, byte[] rect, VectorD2 origin, VectorD2 sizePix) where T : struct {
			r.SetRawRect(rect, (uint)origin.x, (uint)origin.y, (uint)sizePix.width, (uint)sizePix.height);
		}

		public static Raster<float> Scaled(this Raster<float> src, VectorD2 sizePix) {
			return src.Scaled((uint)sizePix.width, (uint)sizePix.width);
		}

		public static Raster<ColorF32> Scaled(this Raster<ColorF32> src, VectorD2 sizePix) {
			return src.Scaled((uint)sizePix.x, (uint)sizePix.y);
		}

		public static Raster<ushort> Scaled(this Raster<ushort> src, VectorD2 sizePix) {
			return src.Scaled((uint)sizePix.x, (uint)sizePix.y);
		}

		public static Raster<byte> Scaled(this Raster<byte> src, VectorD2 sizePix) {
			return src.Scaled((uint)sizePix.x, (uint)sizePix.y);
		}

		public static Raster<ColorU8> Scaled(this Raster<ColorU8> src, VectorD2 sizePix) {
			return src.Scaled((uint)sizePix.x, (uint)sizePix.y);
		}
	}

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
		public double minSampleValue = 0;
		public double maxSampleValue = 0;
		public int noSampleValue = 0;

		public void Rotate(RasterRotation rotation) {
			if ( rotation == RasterRotation.None ) {
				return;
			}

			if (rotation != RasterRotation.CCW_180) {
				sizePix = sizePix.yx;
				pixToProjScale = pixToProjScale.yxz;
			}

			if (tiePoints?.Length > 0) {
				if ( tiePoints.Length > 1 ) {
					Util.Warn("GeoTiffHeader.Rotate only modifying TiePoint[0]. {1} TiePoint(s) not changed.", tiePoints.Length - 1);
				}

				switch (rotation) {
				case RasterRotation.CCW_90:
					tiePoints[0].rasterPt = new VectorD3 { x = 0, y = sizePix.y - 1, z = tiePoints[0].rasterPt.z };
					break;
				case RasterRotation.CCW_180:
					tiePoints[0].rasterPt = new VectorD3 { x = sizePix.x - 1, y = sizePix.y - 1, z = tiePoints[0].rasterPt.z };
					break;
				case RasterRotation.CCW_270:
					tiePoints[0].rasterPt = new VectorD3 { x = sizePix.x - 1, y = 0, z = tiePoints[0].rasterPt.z };
					break;
				}
			}
		}
	}

	public static class IOUtil {
		public static byte[] StructToByteArray<T>(T source) where T : struct {
			T[] sourceArray = new T[] { source };
			GCHandle handle = GCHandle.Alloc(sourceArray, GCHandleType.Pinned);
			try {
				IntPtr pointer = handle.AddrOfPinnedObject();
				byte[] destination = new byte[Marshal.SizeOf(typeof(T))];
				Marshal.Copy(pointer, destination, 0, destination.Length);
				return destination;
			} finally {
				if (handle.IsAllocated)
					handle.Free();
			}
		}

		public static T ByteArrayToStruct<T>(byte[] source) where T : struct {
			T[] destination = new T[1];
			GCHandle handle = GCHandle.Alloc(destination, GCHandleType.Pinned);
			try {
				IntPtr pointer = handle.AddrOfPinnedObject();
				Marshal.Copy(source, 0, pointer, source.Length);
				return destination[0];
			} finally {
				if (handle.IsAllocated)
					handle.Free();
			}
		}
	}

	public struct HeightTileHeader {
		public static readonly uint kMagic = ((uint)Char.GetNumericValue('r') << 24) +
																					((uint)Char.GetNumericValue('9') << 16) +
																					((uint)Char.GetNumericValue('n') << 8) +
																					(uint)Char.GetNumericValue('h');
		public uint magic;
		public uint tileSizePix;
		public uint bytesPerSample;
		public uint isFloat;
		public float pixToMeters;
		public float minTotalTerrainHeight;
		public float maxTotalTerrainHeight;

		public void Init<T>(uint wh, float pixSize, float minV, float maxV) where T : struct {
			magic = kMagic;
			tileSizePix = wh;
			bytesPerSample = (uint)Marshal.SizeOf(typeof(T));
			isFloat = ((typeof(T) == typeof(float)) || (typeof(T) == typeof(double))) ? 1u : 0u;
			pixToMeters = pixSize;
			minTotalTerrainHeight = minV;
			maxTotalTerrainHeight = maxV;
		}

		public void Validate() {
			if (magic != kMagic) {
				throw new Exception(string.Format("incorrect magic number {0} expected {1}", magic, kMagic));
			}

			if (((tileSizePix - 1) & (tileSizePix - 2)) != 0) {
				throw new Exception(string.Format("Height map size {0} is invalid. Must be 2^N + 1.", tileSizePix));
			}

			if (isFloat > 1) {
				throw new Exception(string.Format("Invalid value for isFloat {0}. Must be 0 or 1.", isFloat));
			}

			if ((isFloat != 0) && (bytesPerSample != 4u)) {
				throw new Exception(string.Format("Invalid bytes per sample {0} for sample type float. Must be 4.", bytesPerSample));
			}

			if ((isFloat == 0) && (bytesPerSample != 1u) && (bytesPerSample != 2u)) {
				throw new Exception(string.Format("Invalid bytes per sample {0} for sample type uint. Must be 1 or 2.", bytesPerSample));
			}

			if (pixToMeters <= 0.0f) {
				throw new Exception(string.Format("Invalid pixel to meters scale {0}. Must be > 0", pixToMeters));
			}

			if (minTotalTerrainHeight < 0.0f || maxTotalTerrainHeight < minTotalTerrainHeight) {
				throw new Exception(string.Format("Invalid min/max total terrain heights {0}/{1}. Must be >= 0 and max must be >= min.", minTotalTerrainHeight, maxTotalTerrainHeight));
			}
		}

		public void ValidateCompatible(HeightTileHeader expected) {
			if (bytesPerSample != expected.bytesPerSample) {
				throw new Exception(string.Format("bytesPerSample {0} does not match expected {1}", bytesPerSample, expected.bytesPerSample));
			}

			if (isFloat != expected.isFloat) {
				throw new Exception(string.Format("isFloat {0} does not match expected {1}", isFloat, expected.isFloat));
			}

			if (pixToMeters != expected.pixToMeters) {
				throw new Exception(string.Format("pixSizeM {0} does not match expected {1}", isFloat, expected.isFloat));
			}

			if (minTotalTerrainHeight != expected.minTotalTerrainHeight || maxTotalTerrainHeight != expected.maxTotalTerrainHeight) {
				throw new Exception(string.Format("min/max total terrain heights {0}/{1} do not match expected {2}/{3}",
					minTotalTerrainHeight, maxTotalTerrainHeight, expected.minTotalTerrainHeight, expected.maxTotalTerrainHeight));
			}
		}

		public void ValidateExactMatch(HeightTileHeader expected) {
			ValidateCompatible(expected);
			if (tileSizePix != expected.tileSizePix) {
				throw new Exception(string.Format("tileSizePix {0} does not match expected tileSizePix {1}", tileSizePix, expected.tileSizePix));
			}
		}

		public static HeightTileHeader Read(FileStream inFile) {
			var thisBytes = new byte[Marshal.SizeOf(typeof(HeightTileHeader))];
			inFile.Read(thisBytes, 0, thisBytes.Length);
			HeightTileHeader hdr = IOUtil.ByteArrayToStruct<HeightTileHeader>(thisBytes);

			hdr.Validate();

			return hdr;
		}

		public void Write(FileStream outFile) {
			var thisBytes = IOUtil.StructToByteArray(this);
			outFile.Write(thisBytes, 0, thisBytes.Length);
		}
	}
}
