using System;
using System.Collections.Generic;

namespace GeoTiff2Raw {
	public class Raster<T> {
		public static readonly uint pixSize = getSizeOf(typeof(T));

		public Raster() {
			width = height = pitch = 0;
			pixels = null;
		}

		public Raster(uint _width, uint _height) {
			Init(_width, _height);
		}

		public void Init(uint _width, uint _height) {
			width = _width;
			height = _height;
			pitch = _width * pixSize;
			pixels = new T[width * height];
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

		public Raster<T> Clone() {
			Raster<T> clone = new Raster<T>(width, height);
			Buffer.BlockCopy(pixels, 0, clone.pixels, 0, (int)(pitch * height));
			return clone;
		}

		public Raster<T> Clone(uint x, uint y, uint w, uint h) {
			Raster<T> clone = new Raster<T>(w, h);
			uint dstRow = 0;
			uint srcRow = y * pitch + x * pixSize;
			for (uint r = 0; r < h; r++) {
				Buffer.BlockCopy(pixels, (int)srcRow, clone.pixels, (int)dstRow, (int)clone.pitch);
				srcRow += pitch;
				dstRow += clone.pitch;
			}
			return clone;
		}

		public byte[] ToByteArray() {
			byte[] rasterBytes = new byte[pixels.Length * pixSize];
			Buffer.BlockCopy(pixels, 0, rasterBytes, 0, rasterBytes.Length);
			return rasterBytes;
		}

		public byte[] ToByteArray(uint x, uint y, uint w, uint h) {
			byte[] rasterBytes = new byte[w * h * pixSize];
			uint srcRow = y * pitch + x * pixSize;
			uint dstPitch = w * pixSize;
			uint dstRow = 0;
			for ( uint r = 0; r < h; r++ ) {
				Buffer.BlockCopy(pixels, (int)srcRow, rasterBytes, (int)dstRow, (int)dstPitch);
				srcRow += pitch;
				dstRow += dstPitch;
			}
			return rasterBytes;
		}

		public void GetRow<T2>(uint y, T2[] row) {
			Buffer.BlockCopy(pixels, (int)(y * pitch), row, 0, (int)pitch);
		}

		public void SetRow<T2>(uint y, T2[] row) {
			Buffer.BlockCopy(row, 0, pixels, (int)(y * pitch), (int)pitch);
		}

		public void GetRowSegment<T2>(uint y, uint x, uint pixCount, T2[] segment) {
			Buffer.BlockCopy(pixels, (int)(y * pitch + x * pixSize), segment, 0, (int)(pixCount * pixSize));
		}

		public void SetRowSegment<T2>(uint y, uint x, uint pixCount, T2[] segment) {
			Buffer.BlockCopy(segment, 0, pixels, (int)(y * pitch + x * pixSize), (int)(pixCount * pixSize));
		}

		public void YFlip() {
			T[] tempRow = new T[width];

			for ( int r0 = 0, r1 = (int)((height - 1) * pitch); r0 < r1; r0 += (int)pitch, r1 -= (int)pitch ) {
				Buffer.BlockCopy(pixels, r0, tempRow, 0, (int)pitch);
				Buffer.BlockCopy(pixels, r1, pixels, r0, (int)pitch);
				Buffer.BlockCopy(tempRow, 0, pixels, r1, (int)pitch);
			}
		}

		public void XFlip() {
			for (uint r = 0, endR = width * height; r < endR; r += width) {
				for (uint dstX = 0, srcX = (width - 1); dstX < width; dstX++, srcX--) {
					T t = pixels[r + dstX];
					pixels[r + dstX] = pixels[r + srcX];
					pixels[r + srcX] = t;
				}
			}
		}

		public enum Rotation {
			CCW_90,
			CCW_180,
			CCW_270,
			CW_90 = CCW_270,
			CW_180 = CCW_180,
			CW_270 = CCW_90
		}

		public void Rotate(Rotation r) {
			switch(r) {
			case Rotation.CCW_90:
				rotate90(true);
				break;
			case Rotation.CW_90:
				rotate90(false);
				break;
			case Rotation.CCW_180:
				rotate180();
				break;
			}
		}

		public T[] pixels { get; private set; }
		public uint width { get; private set; }
		public uint height { get; private set; }
		public uint pitch { get; private set; }

		private void rotate90(bool ccw) {
			uint newW = height;
			uint newH = width;
			uint newP = newW * pixSize;
			T[] newPixels = new T[width * height];

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

		private void rotate180() {
			for ( uint dstR = 0, srcR = (height - 1) * width, endR = width * height; dstR < endR; dstR += width, srcR -= width ) {
				for ( uint dstX = 0, srcX = (width - 1); dstX < width; dstX++, srcX-- ) {
					T t = pixels[dstR + dstX];
					pixels[dstR + dstX] = pixels[srcR + srcX];
					pixels[srcR + srcX] = t;
				}
			}
		}

		static uint getSizeOf(Type t) {
			if (t == typeof(long) || t == typeof(ulong) || t == typeof(double)) {
				return 8;
			}
			if (t == typeof(int) || t == typeof(uint) || t == typeof(float)) {
				return 4;
			}
			if (t == typeof(short) || t == typeof(ushort)) {
				return 2;
			}
			if (t == typeof(byte) || t == typeof(sbyte)) {
				return 1;
			}
			throw new Exception(string.Format("{0} is not a numeric type.", t.Name));
		}
	}

	public static class RasterUtil {
		public static Raster<float> Convert(Raster<float> dst, Raster<byte> src, float transation = 0.0f, float scale = 1.0f / byte.MaxValue) {
			dst = new Raster<float>(src.width, src.height);
			for (int i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = (src.pixels[i] + transation) * scale;
			}
			return dst;
		}

		public static Raster<float> Convert(Raster<float> dst, Raster<ushort> src, float transation = 0.0f, float scale = 1.0f / ushort.MaxValue) {
			dst.Init(src.width, src.height);
			for (int i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = (src.pixels[i] + transation) * scale;
			}
			return dst;
		}

		public static Raster<ushort> Convert(Raster<ushort> dst, Raster<float> src, float translation = 0.0f, float scale = ushort.MaxValue, float round = 0.5f) {
			dst.Init(src.width, src.height);
			for (uint i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = (ushort)((src.pixels[i] + translation) * scale + round);
			}
			return dst;
		}

		public static Raster<byte> Convert(Raster<byte> dst, Raster<float> src, float translation = 0.0f, float scale = byte.MaxValue, float round = 0.5f) {
			dst.Init(src.width, src.height);
			for (uint i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = (byte)((src.pixels[i] + translation) * scale + round);
			}
			return dst;
		}

		public static Raster<byte> Convert(Raster<byte> dst, Raster<ushort> src) {
			dst.Init(src.width, src.height);
			for (uint i = 0; i < dst.pixels.Length; i++) {
				dst.pixels[i] = (byte)(src.pixels[i] >> 8);
			}
			return dst;
		}
	}

	public class RasterGrayF32 : Raster<float> {
		public RasterGrayF32() { }

		public RasterGrayF32(uint _width, uint _height) : base(_width, _height) {
		}

		public float GetPixel(float x, float y) {
			uint xlo = (uint)x;
			uint xhi = xlo + 1;
			uint rowLo = (uint)y * width;
			uint rowHi = rowLo + width;
			float fxhi = x - xlo;
			float fxlo = 1.0f - fxhi;
			float fyhi = y - (uint)y;
			float fylo = 1.0f - fyhi;
			return pixels[rowLo + xlo] * fylo * fxlo +
						 pixels[rowLo + xhi] * fylo * fxhi +
						 pixels[rowHi + xlo] * fyhi * fxlo +
						 pixels[rowHi + xhi] * fyhi * fxhi;
		}

		public RasterGrayF32 Scaled(uint w, uint h) {
			if (w == width && h == height) {
				return this;
			}

			if ((w >= width) != (h >= height)) {
				if (w == width || h == height) {
					return scaledDown(w, h);
				}
				if (w > width) {
					return scaledUp(w, height)?.scaledDown(w, h);
				}
				return scaledUp(width, h)?.scaledDown(w, h);
			}

			if (w >= width) {
				return scaledUp(w, h);
			}

			return scaledDown(w, h);
		}

		RasterGrayF32 scaledUp(uint w, uint h) {
			RasterGrayF32 src = this;

			while ((src.width << 1) < w && (src.height << 1) < h) {
				src = src.scaledDown(src.width << 1, src.height << 1);
			}

			while ((src.width << 1) < w) {
				src = src.scaledDown(src.width << 1, src.height);
			}

			while ((src.height << 1) < h) {
				src = src.scaledDown(src.width, src.height << 1);
			}

			RasterGrayF32 dst = new RasterGrayF32(w, h);

			float srcYStep = (float)(height - 1) / (float)(h - 1);
			float srcXStep = (float)(width - 1) / (float)(w - 1);
			float srcY = 0.0f;
			float srcX;

			for (uint dstY = 0; dstY < h; dstY++, srcY += srcYStep) {
				srcX = 0.0f;
				for (uint dstX = 0; dstX < w; dstX++, srcX += srcXStep) {
					float p = src.GetPixel(srcX, srcY);
					dst.SetPixel(dstX, dstY, p);
				}
			}

			return dst;
		}

		RasterGrayF32 scaledDown(uint w, uint h) {
			RasterGrayF32 src = this;

			while ((src.width >> 1) > w && (src.height >> 1) > h) {
				src = src.scaledDown(src.width >> 1, src.height >> 1);
			}

			while ((src.width >> 1) > w) {
				src = src.scaledDown(src.width >> 1, src.height);
			}

			while ((src.height >> 1) > h) {
				src = src.scaledDown(src.width, src.height >> 1);
			}

			RasterGrayF32 dst = new RasterGrayF32(w, h);

			float srcYStep = (float)height / (float)h;
			float srcXStep = (float)width / (float)w;
			float srcY = (srcYStep - 1) * 0.5f;
			float srcX;

			for (uint dstY = 0; dstY < h; dstY++, srcY += srcYStep) {
				srcX = (srcXStep - 1) * 0.5f;
				for (uint dstX = 0; dstX < w; dstX++, srcX += srcXStep) {
					float p = src.GetPixel(srcX, srcY);
					dst.SetPixel(dstX, dstY, p);
				}
			}

			return dst;
		}
	}
}
