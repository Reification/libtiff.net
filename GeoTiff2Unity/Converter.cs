// normally only raw format height tiles are saved.
// enable to save height tiles in tiff format as well.
// useful for debugging height tile output. unity does not handle tiff for import.
#undef SAVE_TIFF_HEIGHT_TILES
using System;
using System.IO;
using System.Collections.Generic;
using BitMiracle.LibTiff.Classic;

//
// TODO: instead of resizing and just cutting off one piece
// break into multiple squares that suit unity requirements.
// number as grid. each raw height map should be 2^N+1 square with size in name on output
// grid number should also be in output name
// same for RGB tifs out - size not needed, grid coords yes.
//

namespace GeoTiff2Unity {
	using GTHeightRaster = Raster<float>;
	using RawHeightRaster = Raster<ushort>;
	using ColorRaster = Raster<ColorU8>;

	public class Converter {
		public const uint kMaxUnityTexSize = 8 * 1024;
		public const uint kBCBlockSize = 4;

		public const uint kMinRGBTexSize = 512;
		public const uint kMaxRGBTexSize = kMaxUnityTexSize;
		public const bool kDefaultRGBScaleToEvenBCBlockSize = true;
		public const uint kMinHeightTexSize = 65;
		public const uint kMaxHeightTexSize = (4 * 1024) + 1;

		public string hmTiffInPath = null;
		public string rgbTiffInPath = null;
		public string outPathBase = null;

		public uint hmOutMaxTexSize = kMaxHeightTexSize;
		public uint rgbOutMaxTexSize = kMaxRGBTexSize;
		public bool rgbScaleToEvenBCBlockSize = kDefaultRGBScaleToEvenBCBlockSize;

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

			computeAlignedTiling();

			processHeightMap();

			processRGBImage();
		}

		void loadHeightMapHeader() {
			hmTiffIn = loadGeoTiffHeader(hmHeader, hmTiffInPath);

			if (hmHeader.channelCount != 1 || hmHeader.sampleFormat != SampleFormat.IEEEFP || hmHeader.bitsPerChannel != 32) {
				Util.Error("Height map has {0} {1} bit {2} channels. Expected exactly 1 32 bit IEEEFP.",
					hmHeader.channelCount, hmHeader.bitsPerChannel, hmHeader.sampleFormat);
			}

			if (hmHeader.pixToProjScale.x != hmHeader.pixToProjScale.y) {
				Util.Error("Height map {0} has non-square {1} pixels!", (VectorD2)hmHeader.pixToProjScale);
				/// TODO: if this becomes a thing we'll need to scale the height map after loading it to correct.
			}
		}

		void loadRGBHeader() {
			rgbTiffIn = loadGeoTiffHeader(rgbHeader, rgbTiffInPath);

			if (rgbHeader.channelCount != 3 || rgbHeader.sampleFormat != SampleFormat.UINT || rgbHeader.bitsPerChannel != 8) {
				Util.Error("{0} has {1} {2} bit {3} channels. Expected 3 8 bit UINT.",
					rgbTiffInPath, rgbHeader.channelCount, rgbHeader.bitsPerChannel, rgbHeader.sampleFormat);
			}
		}

		void computeAlignedTiling() {
			// no scaling - we're going to tile.
			hmOutSizePix = hmHeader.sizePix;
			rgbOutSizePix = rgbHeader.sizePix;

			/// HACK!
			rgbHeader.pixToProjScale = (VectorD2)((VectorD2)(rgbHeader.pixToProjScale)).Max();

			hmToRGBPixScale = hmHeader.pixToProjScale / rgbHeader.pixToProjScale;
			rgbToHMPixScale = rgbHeader.pixToProjScale / hmHeader.pixToProjScale;

			computeAlignment();

			hmOutTileSizePix = (VectorD2)Math.Min(calcHeightMapSizeLTE(hmOutSizePix.Min()), hmOutMaxTexSize);

			VectorD2 rgbOutTileSizePix = hmOutTileSizePix * hmToRGBPixScale;

			while (rgbOutTileSizePix.Max() > rgbOutMaxTexSize) {
				hmOutTileSizePix = (VectorD2)calcHeightMapSizeLTE(hmOutTileSizePix.x - 2);
				rgbOutTileSizePix = (hmOutTileSizePix * hmToRGBPixScale).Round();
			}

			VectorD2 tileCounts = (hmOutSizePix / hmOutTileSizePix).Ceiling();

			Util.Log("\nOutput will be {0} ({1}) tiles.", tileCounts, tileCounts.x * tileCounts.y );
			Util.Log("  Height map tile size: {0}", hmOutTileSizePix);
			Util.Log("  RGB texture tile size: {0}\n", rgbOutTileSizePix);
		}

		void processHeightMap() {
			var hmRasterOut = new RawHeightRaster();
			float hmGT2RawSampleTrans = 0.0f;
			float hmGT2RawSampleScale = 0.0f;

			{
				var hmRasterIn = new GTHeightRaster();

				loadPixelData(ref hmTiffIn, hmHeader, hmRasterIn);

				// Unity height map textures are (by default) bottom to top.
				switch (hmHeader.orientation) {
				case Orientation.TOPLEFT:
					hmRasterIn.YFlip();
					break;
				case Orientation.BOTLEFT:
					break;
				default:
					Util.Error("Unsupported height map orientation {0}", hmHeader.orientation);
					break;
				}

				Util.Log("Converting float{0} geotiff height map to uint{1} Unity ready {2} sized raw tiles.", 
					hmRasterIn.bitsPerPixel,
					hmRasterOut.bitsPerPixel, 
					hmOutTileSizePix);

				hmRasterIn = processHeightMapData(hmRasterIn);

				hmGT2RawSampleTrans = (float)-hmHeader.minSampleValue;
				hmGT2RawSampleScale = (float)((Math.Pow(2.0, hmRasterOut.bitsPerPixel) - 1) / (hmHeader.maxSampleValue + hmGT2RawSampleTrans));

				hmRasterIn.Convert(hmRasterOut, hmGT2RawSampleTrans, hmGT2RawSampleScale);
			}

			VectorD2 hmTileCoords = (VectorD2)0;
			VectorD2 hmTileOrigin = (VectorD2)0;

			uint tileCount = 0;
			for ( ; (hmTileOrigin.y + hmOutTileSizePix.y) < hmOutSizePix.y; hmTileCoords.y++) {
				hmTileCoords.x = 0;
				hmTileOrigin.x = 0;
				for ( ; (hmTileOrigin.x + hmOutTileSizePix.x) < hmOutSizePix.x; hmTileCoords.x++) {
					var hmTile = hmRasterOut.Clone(hmTileOrigin, hmOutTileSizePix);
					string hmTileOutPath = genHMRawOutPath(hmTileCoords, hmTile);
					writeRawOut(hmTileOutPath, hmTile);
					Util.Log("  wrote tile {0}", hmTileOutPath);
					hmTileOrigin.x += hmOutTileSizePix.x;
					tileCount++;
#if SAVE_TIFF_HEIGHT_TILES
					writeRGBTiffOut(hmTileOutPath.Replace(".raw", ".tif"), hmTile, Orientation.BOTLEFT);
#endif
				}
				hmTileOrigin.y += hmOutTileSizePix.y;
			}

			if (hmTileOrigin != hmOutSizePix) {
				Util.Warn("  Unhandled input leftover edges {0}", hmOutSizePix - hmTileOrigin);
			}

			float projMinV = (float)(hmHeader.minSampleValue * hmHeader.pixToProjScale.z);
			float projMaxV = (float)(hmHeader.maxSampleValue * hmHeader.pixToProjScale.z);

			Util.Log("  Pix value range: {0} (0->{0})", (uint)((hmHeader.maxSampleValue + hmGT2RawSampleTrans) * hmGT2RawSampleScale + 0.5f));
			Util.Log("  Geo vertical range: {0} ({1}->{2}) {3}", projMaxV - projMinV, projMinV, projMaxV, hmHeader.geoKeys.verticalLinearUnit);
		}

		void processRGBImage() {
			VectorD2 rgbOutTileSizePix = (hmOutTileSizePix * hmToRGBPixScale).Round();

			var rgbRaster = new ColorRaster();

			loadPixelData(ref rgbTiffIn, rgbHeader, rgbRaster);

			Util.Log("Converting RGB geotiff image to Unity ready {0} sized tiff tiles.", rgbOutTileSizePix);

			rgbRaster = processRGBData(rgbRaster);

			VectorD2 hmTileOrigin = (VectorD2)0;
			VectorD2 rgbTileCoords = (VectorD2)0;
			VectorD2 rgbTileOrigin = (VectorD2)0;

			VectorD2 bcTileSizePix = (rgbOutTileSizePix / kBCBlockSize).Ceiling() * kBCBlockSize;

			if (rgbScaleToEvenBCBlockSize && rgbOutTileSizePix != bcTileSizePix) {
				Util.Log("  RGB tiles will be scaled to {1}, multiple of {0} for compatibility with block compression algorithms.",
					bcTileSizePix,
					kBCBlockSize );
			}

			uint tileCount = 0;
			for (; (hmTileOrigin.y + hmOutTileSizePix.y) < hmOutSizePix.y; rgbTileCoords.y++) {
				hmTileOrigin.x = 0;
				rgbTileCoords.x = 0;
				rgbTileOrigin.x = 0;

				for (; (hmTileOrigin.x + hmOutTileSizePix.x) < hmOutSizePix.x; rgbTileCoords.x++) {
					var rgbTile = rgbRaster.Clone(rgbTileOrigin, rgbOutTileSizePix);

					if ( rgbScaleToEvenBCBlockSize && rgbOutTileSizePix != bcTileSizePix ) {
						rgbTile = rgbTile.Scaled( bcTileSizePix );
					}

					string rgbTileOutPath = genRGBTiffOutPath(rgbTileCoords, rgbTile);
					writeRGBTiffOut(rgbTileOutPath, rgbTile, rgbHeader.orientation);
					Util.Log("  wrote tile {0}", rgbTileOutPath);
					hmTileOrigin.x += hmOutTileSizePix.x;
					rgbTileOrigin.x += rgbOutTileSizePix.x;
					tileCount++;
				}
				hmTileOrigin.y += hmOutTileSizePix.y;
				rgbTileOrigin.y += rgbOutTileSizePix.y;
			}

			if ( rgbTileOrigin != rgbOutSizePix ) {
				Util.Warn("  Unhandled input leftover edges {0}", rgbOutSizePix - rgbTileOrigin);
			}
		}

		static double calcHeightMapSizeLTE(double curSize) {
			// Unity height maps must be size 2^N + 1 
			return Math.Pow(2.0, Math.Floor(Math.Log(curSize) / Math.Log(2.0))) + 1.0;
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
				Util.Error("Mismatch between height map plane units {0} and height map vertical units {1}", hmHeader.geoKeys.projLinearUnit, hmHeader.geoKeys.verticalLinearUnit);
			}

			if (hmHeader.geoKeys.projLinearUnit != rgbHeader.geoKeys.projLinearUnit) {
				Util.Error("Mismatch between height map units {0} and rgb image units {1}", hmHeader.geoKeys.projLinearUnit, rgbHeader.geoKeys.projLinearUnit);
			}

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
				Util.Error("{0} is tiled - only stripped images supported.", tiff.FileName());
			}

			tiff.Dispose();
			tiff = null;
		}

		void writeRGBTiffOut<T>(string path, Raster<T> raster, Orientation orientation) where T : struct {
			using (Tiff outRGB = Tiff.Open(path, "w")) {
				outRGB.SetField(TiffTag.IMAGEWIDTH, (int)raster.width);
				outRGB.SetField(TiffTag.IMAGELENGTH, (int)raster.height);
				outRGB.SetField(TiffTag.SAMPLESPERPIXEL, raster.channelCount);
				outRGB.SetField(TiffTag.BITSPERSAMPLE, raster.bitsPerChannel);
				outRGB.SetField(TiffTag.SAMPLEFORMAT, SampleFormat.UINT);
				outRGB.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
				outRGB.SetField(TiffTag.ORIENTATION, orientation);
				outRGB.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
				outRGB.SetField(TiffTag.COMPRESSION, Compression.LZW);
				outRGB.SetField(TiffTag.PREDICTOR, Predictor.HORIZONTAL);

				const uint rgbOutRowsPerStrip = 32;

				outRGB.SetField(TiffTag.ROWSPERSTRIP, (int)rgbOutRowsPerStrip);

				var tmpStrip = new byte[raster.pitch * rgbOutRowsPerStrip];

				for (uint y = 0, si = 0; y < raster.height; y += rgbOutRowsPerStrip, si++) {
					uint stripRowCount = Math.Min(rgbOutRowsPerStrip, raster.height - y);

					raster.GetRawRows(y, tmpStrip, stripRowCount);
					outRGB.WriteEncodedStrip((int)si, tmpStrip, (int)(stripRowCount * raster.pitch));
				}
			}
		}

		void writeRawOut<T>(string path, Raster<T> raster) where T : struct {
			using (var outFile = new FileStream(path, FileMode.Create, FileAccess.Write)) {
				outFile.Write(raster.ToByteArray(), 0, (int)raster.sizeBytes);
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

		string genHMRawOutPath(VectorD2 tilePos, RawHeightRaster hmRaster) {
			string hmRawOutPathFmt = outPathBase + "_HM_{0}x{1}xU{2}_{3:D2}-{4:D2}.raw";
			string path = string.Format(hmRawOutPathFmt, hmRaster.width, hmRaster.height, hmRaster.bitsPerChannel, (uint)tilePos.y, (uint)tilePos.x);
			return path;
		}

		string genRGBTiffOutPath(VectorD2 tilePos, ColorRaster rgbRaster) {
			string rgbTiffOutPathFmt = outPathBase + "_RGB_{0:D2}-{1:D2}.tif";
			string path = string.Format(rgbTiffOutPathFmt, (uint)tilePos.y, (uint)tilePos.x);
			return path;
		}

		Tiff hmTiffIn = null;
		GeoTiffHeader hmHeader = new GeoTiffHeader();

		VectorD2 hmOutSizePix = (VectorD2)0;

		Tiff rgbTiffIn = null;
		GeoTiffHeader rgbHeader = new GeoTiffHeader();

		VectorD2 rgbOutSizePix = (VectorD2)0;

		VectorD2 hmToRGBPixScale = (VectorD2)0;
		VectorD2 rgbToHMPixScale = (VectorD2)0;
		// may be used for handling misalignment between height and tiff - not needed for current data sources.
		//VectorD2 hmToRGBPixTrans = (VectorD2)0;
		//VectorD2 rgbToHMPixTrans = (VectorD2)0;

		/// @TODO: sizes for additional horizontal and verticlal tiles
		/// to cover as much of heightmap as can be done with sums of legally sized tiles.
		VectorD2 hmOutTileSizePix = (VectorD2)0;
		List<VectorD2> hmSubTileSizePixList = new List<VectorD2>();
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

		public static Raster<float> Scaled(this Raster<float> src, VectorD2 sizePix) {
			return src.Scaled((uint)sizePix.width, (uint)sizePix.width);
		}

		public static Raster<ColorF32> Scaled(this Raster<ColorF32> src, VectorD2 sizePix) {
			return src.Scaled((uint)sizePix.x, (uint)sizePix.y);
		}

		public static Raster<ushort> Scaled(this Raster<ushort> src, VectorD2 sizePix) {
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
	}
}
