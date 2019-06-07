using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GeoTiff2Unity {
	public enum RasterRotation {
		CCW_90,
		CCW_180,
		CCW_270,
		CW_90 = CCW_270,
		CW_180 = CCW_180,
		CW_270 = CCW_90
	}

	public class Raster<T> where T : struct {
		public static readonly uint bytesPerPixel = getPixTypeSizeBytes();
		public static readonly uint channelCount = getPixTypeChannelCount();
		public static readonly Type pixelType = typeof(T);
		public static readonly Type channelType = getPixChannelType();

		public static uint bytesPerChannel { get { return bytesPerPixel / channelCount; } }
		public static uint bitsPerPixel { get { return (bytesPerPixel * 8); } }
		public static uint bitsPerChannel { get { return (bytesPerPixel * 8) / channelCount; } }

		public static bool channelIsFloat { get { return (channelType == typeof(float) || channelType == typeof(double)); } }
		public static bool channelIsUint { get { return !channelIsFloat; } }

		public static uint maxChannelValue {
			get {
				return channelIsFloat ? 1 : (uint)((1ul << (int)bitsPerChannel) - 1);
			}
		}

		public static string pixelTypeName {
			get {
				return pixelType.IsPrimitive ? channelTypeName : pixelType.Name;
			}
		}

		public static string channelTypeName {
			get {
				string baseType = channelIsFloat ? "float" : "uint";
				return string.Format("{0}{1}", baseType, bitsPerChannel);
			}
		}

		public bool getChannelIsUint() { return channelIsUint; }
		public bool getChannelIsFloat() { return channelIsFloat; }
		public uint getMaxChannelValue() { return maxChannelValue; }
		public uint getBytesPerPixel() { return bytesPerPixel; }
		public uint getBytesPerChanne() { return bytesPerChannel; }
		public uint getBitsPerPixel() { return bitsPerPixel; }
		public uint getBitsPerChannel() { return bitsPerChannel; }
		public uint getChannelCount() { return channelCount; }
		public Type getPixelType() { return pixelType; }
		public Type getChannelType() { return channelType; }
		public string getPixelTypeName() { return pixelTypeName; }
		public string getChannelTypeName() { return channelTypeName; }

		public Raster() {
			width = height = pitch = 0;
			pixels = null;
		}

		public Raster(uint _width, uint _height) {
			Init(_width, _height);
		}

		public void Init(uint _width, uint _height) {
			if (_width != width || _height != height) {
				width = _width;
				height = _height;
				pitch = _width * bytesPerPixel;
			}
			if (!(pixels?.Length == width * height)) {
				pixels = new T[width * height];
			}
		}

		public Raster<T> Clone() {
			Raster<T> clone = new Raster<T>(width, height);
			Array.Copy(pixels, 0, clone.pixels, 0, pixels.Length);
			return clone;
		}

		public Raster<T> Clone(uint x, uint y, uint w, uint h) {
			Raster<T> clone = new Raster<T>(w, h);
			var pix = clone.pixels;
			GetRect(ref pix, x, y, w, h);
			if (pix != clone.pixels) { throw new Exception("internal error: allocation should only occur for null or undersized buffers!"); }
			return clone;
		}

		public T GetPixel(uint x, uint y) {
			return pixels[y * width + x];
		}

		public void SetPixel(uint x, uint y, T p) {
			pixels[y * width + x] = p;
		}

		public void Clear(T clearVal) {
			for (int i = 0; i < pixels.Length; i++) {
				pixels[i] = clearVal;
			}
		}

		public void Clear(T clearVal, uint x, uint y, uint w, uint h) {
			uint dstRow = (y * width) + x;
			for (uint r = 0; r < h; r++) {
				for(uint c = 0; c < w; c++) {
					pixels[dstRow + c] = clearVal;
				}
				dstRow += width;
			}
		}

		public void GetRows(uint y, ref T[] rows, uint rowCount) {
			int pixCount = (int)(width * rowCount);
			if (!(rows?.Length >= pixCount)) {
				rows = new T[pixCount];
			}
			Array.Copy(pixels, (int)(y * width), rows, 0, pixCount);
		}

		public void SetRows(uint y, T[] rows, uint rowCount) {
			Array.Copy(rows, 0, pixels, (int)(y * width), (int)(width * rowCount));
		}

		public void GetRow(uint y, ref T[] row) {
			GetRows(y, ref row, 1);
		}

		public void SetRow(uint y, T[] row) {
			SetRows(y, row, 1);
		}

		public void GetRect(ref T[] rect, uint x, uint y, uint w, uint h) {
			uint dstRow = 0;
			uint srcRow = y * width + x;
			if (!(rect?.Length >= (w * h))) {
				rect = new T[w * h];
			}
			for (uint r = 0; r < h; r++) {
				Array.Copy(pixels, (int)srcRow, rect, (int)dstRow, (int)w);
				srcRow += width;
				dstRow += w;
			}
		}

		public void GetRect(Raster<T> rect, uint x, uint y) {
			var pix = rect.pixels;
			GetRect(ref pix, x, y, rect.width, rect.height);
			if (pix != rect.pixels) { throw new Exception("internal error: allocation should only occur for null or undersized buffers!"); }
		}

		public void SetRect(T[] rect, uint x, uint y, uint w, uint h) {
			uint dstRow = 0;
			uint srcRow = y * width + x;
			for (uint r = 0; r < h; r++) {
				Array.Copy(pixels, (int)srcRow, rect, (int)dstRow, (int)w);
				srcRow += width;
				dstRow += w;
			}
		}

		public void SetRect(Raster<T> rect, uint x, uint y) {
			SetRect(rect.pixels, x, y, rect.width, rect.height);
		}

		public byte[] CloneRaw() {
			byte[] rasterBytes = new byte[sizeBytes];

			if (typeof(T).IsPrimitive) {
				Buffer.BlockCopy(pixels, 0, rasterBytes, 0, rasterBytes.Length);
			} else {
				RasterUtil.StructArrayToByteArray<T>(pixels, ref rasterBytes);
			}

			return rasterBytes;
		}

		public byte[] CloneRaw(uint x, uint y, uint w, uint h) {
			byte[] rectBytes = null;
			GetRawRect(ref rectBytes, x, y, w, h);
			return rectBytes;
		}

		public void GetRawRows(uint y, ref byte[] rows, uint rowCount) {
			int byteCount = (int)(pitch * rowCount);
			if (typeof(T).IsPrimitive) {
				if (!(rows?.Length >= byteCount)) {
					rows = new byte[byteCount];
				}
				Buffer.BlockCopy(pixels, (int)(y * pitch), rows, 0, byteCount);
			} else {
				T[] trows = null;
				GetRows(y, ref trows, rowCount);
				rows = RasterUtil.StructArrayToByteArray(trows);
			}
		}

		public void SetRawRows(uint y, byte[] rows, uint rowCount) {
			if (typeof(T).IsPrimitive) {
				Buffer.BlockCopy(rows, 0, pixels, (int)(y * pitch), (int)(pitch * rowCount));
			} else {
				SetRows(y, RasterUtil.ByteArrayToStructArray<T>(rows), rowCount);
			}
		}

		public void GetRawRect(ref byte[] rect, uint x, uint y, uint w, uint h) {
			int byteCount = (int)(w * h * bytesPerPixel);
			if (!(rect?.Length >= byteCount)) {
				rect = new byte[byteCount];
			}

			if (typeof(T).IsPrimitive) {
				uint srcR = (y * pitch) + (x * bytesPerPixel);
				uint dstR = 0;
				uint dstPitch = w * bytesPerPixel;

				for (uint r = 0; r < h; r++) {
					Buffer.BlockCopy(pixels, (int)srcR, rect, (int)dstR, (int)dstPitch);
					srcR += pitch;
					dstR += dstPitch;
				}
			} else {
				T[] trect = null;
				GetRect(ref trect, x, y, w, h);
				RasterUtil.StructArrayToByteArray(trect, ref rect);
			}
		}

		public void SetRawRect(byte[] rect, uint x, uint y, uint w, uint h) {
			int byteCount = (int)(w * h * bytesPerPixel);

			if (typeof(T).IsPrimitive) {
				uint dstR = (y * pitch) + (x * bytesPerPixel);
				uint srcR = 0;
				uint srcPitch = w * bytesPerPixel;

				for (uint r = 0; r < h; r++) {
					Buffer.BlockCopy(rect, (int)srcR, pixels, (int)dstR, (int)srcPitch);
					srcR += srcPitch;
					dstR += pitch;
				}
			} else {
				SetRect(RasterUtil.ByteArrayToStructArray<T>(rect), x, y, w, h);
			}
		}

		public void YFlip() {
			T[] tempRow = new T[width];

			for (int r0 = 0, r1 = (int)((height - 1) * width); r0 < r1; r0 += (int)width, r1 -= (int)width) {
				Array.Copy(pixels, r0, tempRow, 0, (int)width);
				Array.Copy(pixels, r1, pixels, r0, (int)width);
				Array.Copy(tempRow, 0, pixels, r1, (int)width);
			}
		}

		public void XFlip() {
			for (uint r = 0, endR = width * height; r < endR; r += width) {
				for (uint dstX = 0, srcX = (width - 1), w = width / 2; dstX < w; dstX++, srcX--) {
					T a = pixels[r + srcX];
					T b = pixels[r + dstX];
					pixels[r + dstX] = a;
					pixels[r + srcX] = b;
				}
			}
		}

		public void Rotate(RasterRotation r) {
			switch (r) {
			case RasterRotation.CCW_90:
				rotate90(true);
				break;
			case RasterRotation.CW_90:
				rotate90(false);
				break;
			case RasterRotation.CCW_180:
				YFlip();
				XFlip();
				break;
			}
		}

		public T[] pixels { get; private set; }
		public uint width { get; private set; }
		public uint height { get; private set; }
		public uint pitch { get; private set; }

		public uint sizeBytes { get { return pitch * height; } }
		public uint sizePix { get { return width * height; } }

		private void rotate90(bool ccw) {
			uint newW = height;
			uint newH = width;
			uint newP = newW * bytesPerPixel;
			T[] newPixels = new T[sizePix];

			if (ccw) {
				for (uint dstY = 0, dstR = 0, srcX = width - 1; dstY < newH; dstY++, dstR += newW, srcX--) {
					for (uint dstX = 0, srcR = 0; dstX < newW; dstX++, srcR += width) {
						newPixels[dstR + dstX] = pixels[srcR + srcX];
					}
				}
			} else {
				uint lastSrcR = (height - 1) * width;
				for (uint dstY = 0, dstR = 0, srcX = 0; dstY < newH; dstY++, dstR += newW, srcX++) {
					for (uint dstX = 0, srcR = lastSrcR; dstX < newW; dstX++, srcR -= width) {
						newPixels[dstR + dstX] = pixels[srcR + srcX];
					}
				}
			}

			width = newW;
			height = newH;
			pitch = newP;
			pixels = newPixels;
		}

		private static uint getPixTypeSizeBytes() {
			return (uint)Marshal.SizeOf(typeof(T));
		}

		private static uint getPixTypeChannelCount() {
			return (typeof(T).IsPrimitive ? 1u : 3u);
		}

		private static Type getPixChannelType() {
			Type t = typeof(T);
			if (t.IsByRef) {
				throw new Exception(string.Format("Invalid pixel type {0}, must be primitive or composed of public primitives.", t));
			}
			if (t.IsPrimitive) {
				return t;
			}

			var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

			if (!fields[0].FieldType.IsPrimitive || !fields[0].IsPublic) {
				throw new Exception(string.Format("Invalid pixel type {0}, must be primitive or composed of identical public primitives.", t));
			}

			for (int i = 1; i < fields.Length; i++) {
				if (fields[i].FieldType != fields[0].FieldType || !fields[i].IsPublic) {
					throw new Exception(string.Format("Invalid pixel type {0}, must be primitive or composed of identical public primitives.", t));
				}
			}

			return fields[0].FieldType;
		}
	}

	public struct ColorF32 {
		public float r;
		public float g;
		public float b;

		public ColorU8 ToColorU8(float translation = 0.0f, float scale = byte.MaxValue) {
			return new ColorU8 {
				r = (byte)((r + translation) * scale),
				g = (byte)((g + translation) * scale),
				b = (byte)((b + translation) * scale)
			};
		}

		public ColorU16 ToColorU16(float translation = 0.0f, float scale = ushort.MaxValue) {
			return new ColorU16 {
				r = (ushort)((r + translation) * scale),
				g = (ushort)((g + translation) * scale),
				b = (ushort)((b + translation) * scale)
			};
		}

		public static readonly ColorF32 zero = new ColorF32 { r = 0, g = 0, b = 0 };
	}

	public struct ColorU8 {
		public byte r;
		public byte g;
		public byte b;

		public ColorF32 ToColorF32(float translation = 0.0f, float scale = 1.0f / byte.MaxValue) {
			return new ColorF32 {
				r = (r + translation) * scale,
				g = (g + translation) * scale,
				b = (b + translation) * scale
			};
		}

		public static readonly ColorU8 zero = new ColorU8 { r = 0, g = 0, b = 0 };
	}

	public struct ColorU16 {
		public ushort r;
		public ushort g;
		public ushort b;

		public ColorF32 ToColorF32(float translation = 0.0f, float scale = 1.0f / ushort.MaxValue) {
			return new ColorF32 {
				r = (r + translation) * scale,
				g = (g + translation) * scale,
				b = (b + translation) * scale
			};
		}

		public ColorU8 ToColorU8() {
			return new ColorU8 {
				r = (byte)(r >> 8),
				g = (byte)(g >> 8),
				b = (byte)(b >> 8)
			};
		}

		public ColorU8 divBy4U8() { return new ColorU8 { r = (byte)(r >> 2), g = (byte)(g >> 2), b = (byte)(b >> 2) }; }
		public void accum(ColorU8 c) { r += c.r; g += c.g; b += c.b; }

		public static readonly ColorU16 zero = new ColorU16 { r = 0, g = 0, b = 0 };
	}

	public static class RasterUtil {
		public static byte[] StructArrayToByteArray<T>(T[] source, ref byte[] destination) where T : struct {
			GCHandle handle = GCHandle.Alloc(source, GCHandleType.Pinned);
			try {
				int byteCount = source.Length * Marshal.SizeOf(typeof(T));
				IntPtr pointer = handle.AddrOfPinnedObject();
				if ( !(destination?.Length >= byteCount) ) {
					destination = new byte[byteCount];
				}
				Marshal.Copy(pointer, destination, 0, source.Length);
				return destination;
			} finally {
				if (handle.IsAllocated)
					handle.Free();
			}
		}

		public static T[] ByteArrayToStructArray<T>(byte[] source, ref T[] destination) where T : struct {
			int instanceCount = source.Length / Marshal.SizeOf(typeof(T));
			if (!(destination?.Length >= instanceCount)) {
				destination = new T[instanceCount];
			}
			GCHandle handle = GCHandle.Alloc(destination, GCHandleType.Pinned);
			try {
				IntPtr pointer = handle.AddrOfPinnedObject();
				Marshal.Copy(source, 0, pointer, source.Length);
				return destination;
			} finally {
				if (handle.IsAllocated)
					handle.Free();
			}
		}

		public static byte[] StructArrayToByteArray<T>(T[] source) where T : struct {
			byte[] dst = null;
			return StructArrayToByteArray(source, ref dst);
		}

		public static T[] ByteArrayToStructArray<T>(byte[] source) where T : struct {
			T[] destination = null;
			return ByteArrayToStructArray<T>(source, ref destination);
		}

		public static Raster<float> Convert(this Raster<byte> src, Raster<float> dst, float translation = 0.0f, float scale = 1.0f / byte.MaxValue) {
			dst.Init(src.width, src.height);
			for (int i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = (src.pixels[i] + translation) * scale;
			}
			return dst;
		}

		public static Raster<float> Convert(this Raster<ushort> src, Raster<float> dst, float translation = 0.0f, float scale = 1.0f / ushort.MaxValue) {
			dst.Init(src.width, src.height);
			for (int i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = (src.pixels[i] + translation) * scale;
			}
			return dst;
		}

		public static Raster<ushort> Convert(this Raster<float> src, Raster<ushort> dst, float translation = 0.0f, float scale = ushort.MaxValue, float round = 0.5f) {
			dst.Init(src.width, src.height);
			for (uint i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = (ushort)((src.pixels[i] + translation) * scale + round);
			}
			return dst;
		}

		public static Raster<byte> Convert(this Raster<float> src, Raster<byte> dst, float translation = 0.0f, float scale = byte.MaxValue, float round = 0.5f) {
			dst.Init(src.width, src.height);
			for (uint i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = (byte)((src.pixels[i] + translation) * scale + round);
			}
			return dst;
		}

		public static Raster<float> Convert(this Raster<float> src, Raster<float> dst, float translation = 0.0f, float scale = 1.0f) {
			dst.Init(src.width, src.height);
			for (uint i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = (src.pixels[i] + translation) * scale;
			}
			return dst;
		}

		public static Raster<byte> Convert(this Raster<ushort> src, Raster<byte> dst) {
			dst.Init(src.width, src.height);
			for (uint i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = (byte)(src.pixels[i] >> 8);
			}
			return dst;
		}

		public static Raster<ColorF32> Convert(this Raster<ColorU8> src, Raster<ColorF32> dst, float translation = 0.0f, float scale = 1.0f / byte.MaxValue) {
			dst.Init(src.width, src.height);
			for (int i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = src.pixels[i].ToColorF32(translation, scale);
			}
			return dst;
		}

		public static Raster<ColorU8> Convert(this Raster<ColorF32> src, Raster<ColorU8> dst, float translation = 0.0f, float scale = byte.MaxValue) {
			dst.Init(src.width, src.height);
			for (int i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = src.pixels[i].ToColorU8(translation, scale);
			}
			return dst;
		}

		public static Raster<T2> Convert<T1, T2>(this Raster<T1> src, Raster<T2> dst, Func<T1, T2> convertPix) where T1 : struct where T2 : struct {
			dst.Init(src.width, src.height);
			for (uint i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = convertPix(src.pixels[i]);
			}
			return dst;
		}

		public static float GetSubPixel(this Raster<float> img, float x, float y) {
			uint xlo = (uint)x;
			uint xhi = xlo + 1;
			uint rowLo = (uint)y * img.width;
			uint rowHi = rowLo + img.width;
			float fxhi = x - xlo;
			float fxlo = 1.0f - fxhi;
			float fyhi = y - (uint)y;
			float fylo = 1.0f - fyhi;

			var aaf = fylo * fxlo;
			var abf = fylo * fxhi;
			var baf = fyhi * fxlo;
			var bbf = fyhi * fxhi;

			var aa = img.pixels[rowLo + xlo];
			var ab = abf != 0 ? img.pixels[rowLo + xhi] : 0;
			var ba = baf != 0 ? img.pixels[rowHi + xlo] : 0;
			var bb = bbf != 0 ? img.pixels[rowHi + xhi] : 0;

			return aa * aaf +
						 ab * abf +
						 ba * baf +
						 bb * bbf;
		}

		public static ColorF32 GetSubPixel(this Raster<ColorF32> img, float x, float y) {
			uint xlo = (uint)x;
			uint xhi = xlo + 1;
			uint rowLo = (uint)y * img.width;
			uint rowHi = rowLo + img.width;
			float fxhi = x - xlo;
			float fxlo = 1.0f - fxhi;
			float fyhi = y - (uint)y;
			float fylo = 1.0f - fyhi;

			var aaf = fylo * fxlo;
			var abf = fylo * fxhi;
			var baf = fyhi * fxlo;
			var bbf = fyhi * fxhi;

			var aa = img.pixels[rowLo + xlo];
			var ab = abf != 0 ? img.pixels[rowLo + xhi] : ColorF32.zero;
			var ba = baf != 0 ? img.pixels[rowHi + xlo] : ColorF32.zero;
			var bb = bbf != 0 ? img.pixels[rowHi + xhi] : ColorF32.zero;

			return new ColorF32 {
				r = aa.r * aaf + ab.r * abf + ba.r * baf + bb.r * bbf,
				g = aa.g * aaf + ab.g * abf + ba.g * baf + bb.g * bbf,
				b = aa.b * aaf + ab.b * abf + ba.b * baf + bb.b * bbf
			};
		}

		public static Raster<float> Scaled(this Raster<float> src, uint w, uint h) {
			if (w == src.width && h == src.height) {
				return src;
			}

			if ((w >= src.width) != (h >= src.height)) {
				if (w == src.width || h == src.height) {
					return scaledDown(src, w, h);
				}
				if (w > src.width) {
					return scaledDown(scaledUp(src, w, src.height), w, h);
				}
				return scaledDown(scaledUp(src, src.width, h), w, h);
			}

			if (w >= src.width) {
				return scaledUp(src, w, h);
			}

			return scaledDown(src, w, h);
		}

		public static Raster<ColorF32> Scaled(this Raster<ColorF32> src, uint w, uint h) {
			if (w == src.width && h == src.height) {
				return src;
			}

			if ((w >= src.width) != (h >= src.height)) {
				if (w == src.width || h == src.height) {
					return scaledDown(src, w, h);
				}
				if (w > src.width) {
					return scaledDown(scaledUp(src, w, src.height), w, h);
				}
				return scaledDown(scaledUp(src, src.width, h), w, h);
			}

			if (w >= src.width) {
				return scaledUp(src, w, h);
			}

			return scaledDown(src, w, h);
		}

		public static Raster<byte> Scaled(this Raster<byte> src, uint w, uint h) {
			Raster<byte> dst = src;

			while ((w << 1) >= dst.width && (h << 1) >= dst.height) {
				dst = dst.scaledDown2to1();
			}

			if (dst.width != w || dst.height != h) {
				dst = dst.Convert(new Raster<float>()).Scaled(w, h).Convert(dst);
			}

			return dst;
		}

		public static Raster<ushort> Scaled(this Raster<ushort> src, uint w, uint h) {
			Raster<ushort> dst = src;

			while ((w << 1) >= dst.width && (h << 1) >= dst.height) {
				dst = dst.scaledDown2to1();
			}

			if (dst.width != w || dst.height != h) {
				dst = dst.Convert(new Raster<float>()).Scaled(w, h).Convert(dst);
			}

			return dst;
		}

		public static Raster<ColorU8> Scaled(this Raster<ColorU8> src, uint w, uint h) {
			Raster<ColorU8> dst = src;

			while ((w * 2) <= dst.width && (h * 2) <= dst.height) {
				dst = dst.scaledDown2to1();
			}

			if (dst.width != w || dst.height != h) {
				dst = dst.Convert(new Raster<ColorF32>()).Scaled(w, h).Convert(dst);
			}

			return dst;
		}

		static Raster<ColorU8> scaledDown2to1(this Raster<ColorU8> src) {
			var dst = new Raster<ColorU8>(src.width / 2, src.height / 2);
			var accumRow = new ColorU16[dst.width];

			for (uint i = 0; i < accumRow.Length; i++) {
				accumRow[i] = ColorU16.zero;
			}

			for (uint dstR = 0, srcR = 0, endDstR = dst.width * dst.height; dstR < endDstR; dstR += dst.width) {
				for (int i = 0; i < 2; i++) {
					for (uint dstX = 0, srcX = 0; dstX < dst.width; dstX++, srcX += 2) {
						accumRow[dstX].accum(src.pixels[srcR + srcX]);
						accumRow[dstX].accum(src.pixels[srcR + srcX + 1]);
					}
					srcR += src.width;
				}
				for (uint dstX = 0; dstX < dst.width; dstX++) {
					dst.pixels[dstR + dstX] = accumRow[dstX].divBy4U8();
					accumRow[dstX] = ColorU16.zero;
				}
			}

			return dst;
		}

		static Raster<byte> scaledDown2to1(this Raster<byte> src) {
			var dst = new Raster<byte>(src.width / 2, src.height / 2);
			var accumRow = new ushort[dst.width];

			for (uint i = 0; i < accumRow.Length; i++) {
				accumRow[i] = 0;
			}

			for (uint dstR = 0, srcR = 0, endDstR = dst.width * dst.height; dstR < endDstR; dstR += dst.width) {
				for (int i = 0; i < 2; i++) {
					for (uint dstX = 0, srcX = 0; dstX < dst.width; dstX++, srcX += 2) {
						accumRow[dstX] += src.pixels[srcR + srcX];
						accumRow[dstX] += src.pixels[srcR + srcX + 1];
					}
					srcR += src.width;
				}
				for (uint dstX = 0; dstX < dst.width; dstX++) {
					dst.pixels[dstR + dstX] = (byte)(accumRow[dstX] >> 2);
					accumRow[dstX] = 0;
				}
			}

			return dst;
		}

		static Raster<ushort> scaledDown2to1(this Raster<ushort> src) {
			var dst = new Raster<ushort>(src.width / 2, src.height / 2);
			var accumRow = new uint[dst.width];

			for (uint i = 0; i < accumRow.Length; i++) {
				accumRow[i] = 0;
			}

			for (uint dstR = 0, srcR = 0, endDstR = dst.width * dst.height; dstR < endDstR; dstR += dst.width) {
				for (int i = 0; i < 2; i++) {
					for (uint dstX = 0, srcX = 0; dstX < dst.width; dstX++, srcX += 2) {
						accumRow[dstX] += src.pixels[srcR + srcX];
						accumRow[dstX] += src.pixels[srcR + srcX + 1];
					}
					srcR += src.width;
				}
				for (uint dstX = 0; dstX < dst.width; dstX++) {
					dst.pixels[dstR + dstX] = (ushort)(accumRow[dstX] >> 2);
					accumRow[dstX] = 0;
				}
			}

			return dst;
		}

		private static Raster<float> scaledUp(Raster<float> src, uint w, uint h) {
			while ((src.width << 1) < w && (src.height << 1) < h) {
				src = scaledUp(src, src.width << 1, src.height << 1);
			}

			while ((src.width << 1) < w) {
				src = scaledUp(src, src.width << 1, src.height);
			}

			while ((src.height << 1) < h) {
				src = scaledUp(src, src.width, src.height << 1);
			}

			var dst = new Raster<float>(w, h);

			float srcYStep = (float)(src.height - 1) / (float)(h - 1);
			float srcXStep = (float)(src.width - 1) / (float)(w - 1);
			float srcY = 0.0f;
			float srcX;

			for (uint dstY = 0; dstY < h;) {
				srcX = 0.0f;
				for (uint dstX = 0; dstX < w;) {
					var p = src.GetSubPixel(srcX, srcY);
					dst.SetPixel(dstX, dstY, p);
					dstX++;
					srcX = dstX * srcXStep;
				}
				dstY++;
				srcY = dstY * srcXStep;
			}

			return dst;
		}

		private static Raster<float> scaledDown(Raster<float> src, uint w, uint h) {
			while ((src.width >> 1) > w && (src.height >> 1) > h) {
				src = scaledDown(src, src.width >> 1, src.height >> 1);
			}

			while ((src.width >> 1) > w) {
				src = scaledDown(src, src.width >> 1, src.height);
			}

			while ((src.height >> 1) > h) {
				src = scaledDown(src, src.width, src.height >> 1);
			}

			var dst = new Raster<float>(w, h);

			float srcYStep = (float)src.height / (float)h;
			float srcXStep = (float)src.width / (float)w;
			float srcY = (srcYStep - 1) * 0.5f;
			float srcX;

			for (uint dstY = 0; dstY < h;) {
				srcX = (srcXStep - 1) * 0.5f;
				for (uint dstX = 0; dstX < w;) {
					var p = src.GetSubPixel(srcX, srcY);
					dst.SetPixel(dstX, dstY, p);
					dstX++;
					srcX = dstX * srcXStep;
				}
				dstY++;
				srcY = dstY * srcYStep;
			}

			return dst;
		}

		private static Raster<ColorF32> scaledUp(Raster<ColorF32> src, uint w, uint h) {
			while ((src.width << 1) < w && (src.height << 1) < h) {
				src = scaledUp(src, src.width << 1, src.height << 1);
			}

			while ((src.width << 1) < w) {
				src = scaledUp(src, src.width << 1, src.height);
			}

			while ((src.height << 1) < h) {
				src = scaledUp(src, src.width, src.height << 1);
			}

			var dst = new Raster<ColorF32>(w, h);

			float srcYStep = (float)(src.height - 1) / (float)(h - 1);
			float srcXStep = (float)(src.width - 1) / (float)(w - 1);
			float srcY = 0.0f;
			float srcX;

			for (uint dstY = 0; dstY < h; ) {
				srcX = 0.0f;
				for (uint dstX = 0; dstX < w; ) {
					var p = src.GetSubPixel(srcX, srcY);
					dst.SetPixel(dstX, dstY, p);
					dstX++;
					srcX = dstX * srcXStep;
				}
				dstY++;
				srcY = dstY * srcXStep;
			}

			return dst;
		}

		private static Raster<ColorF32> scaledDown(Raster<ColorF32> src, uint w, uint h) {
			while ((src.width >> 1) > w && (src.height >> 1) > h) {
				src = scaledDown(src, src.width >> 1, src.height >> 1);
			}

			while ((src.width >> 1) > w) {
				src = scaledDown(src, src.width >> 1, src.height);
			}

			while ((src.height >> 1) > h) {
				src = scaledDown(src, src.width, src.height >> 1);
			}

			var dst = new Raster<ColorF32>(w, h);

			float srcYStep = (float)src.height / (float)h;
			float srcXStep = (float)src.width / (float)w;
			float srcY = (srcYStep - 1) * 0.5f;
			float srcX;

			for (uint dstY = 0; dstY < h; ) {
				srcX = (srcXStep - 1) * 0.5f;
				for (uint dstX = 0; dstX < w; ) {
					var p = src.GetSubPixel(srcX, srcY);
					dst.SetPixel(dstX, dstY, p);
					dstX++;
					srcX = dstX * srcXStep;
				}
				dstY++;
				srcY = dstY * srcYStep;
			}

			return dst;
		}
	}
}
