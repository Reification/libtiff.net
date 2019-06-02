﻿using System;
using System.IO;

namespace GeoTiff2Unity {
	class Program {
		static private string appName = "GeoTiff2Raw";
		static private string[] usageText = {
			appName + " <inputHeight.tif> <inputRGB.tif> [-maxheighttex=<size_in_pix>] [-maxrgbtex=<size_in_pix>] <outputNameBase>",
			"  <inputHeight.tif>: source 32 bit float height map image",
			"  <inputRGB.tif>: source RGB texture matching height map",
			"  -maxheighttex=<size_in_pix>: optional. ",
			"    value must be in range " + string.Format("[{0}, {1}]", Converter.kMinHeightTexSize, Converter.kMaxHeightTexSize) + ". default is " + Converter.kMaxHeightTexSize + ".",
			"    vaue must be (2^n + 1), e.g. 129, 257, etc.",
			"  -maxrgbtex=<size_in_pix>: optional.",
			"    value must be in range " + string.Format("[{0}, {1}]", Converter.kMinRGBTexSize, Converter.kMaxRGBTexSize) + ". default is " + Converter.kMaxRGBTexSize + ".",
			"  <outputNameBase>: base name for output height map and rgb assets.",
		};

		static void Main(string[] args) {
			Converter cnv = new Converter();

			foreach (var arg in args) {
				if (arg[0] == '-') {
					string option = arg.Substring(1).ToLower();
					string[] nameValPair = option.Split('=');
					switch (nameValPair[0].ToLower()) {
					case "h":
					case "help":
					case "u":
					case "usage":
						usage(null);
						break;
					case "maxheighttex":
						if(nameValPair.Length != 2) {
							usage("option {0}: invalid format {1}", nameValPair[0], option);
						}
						if ( !uint.TryParse(nameValPair[1], out cnv.hmOutMaxTexSize) || 
									cnv.hmOutMaxTexSize < Converter.kMinHeightTexSize ||
									cnv.hmOutMaxTexSize > Converter.kMaxHeightTexSize ||
									((cnv.hmOutMaxTexSize - 1) & (cnv.hmOutMaxTexSize - 2)) != 0)
						{
							usage("option {0}: invalid value {1}", nameValPair[0], nameValPair[1] );
						}
						break;
					case "maxrgbtex":
						if (nameValPair.Length != 2) {
							usage("option {0}: invalid format {1}", nameValPair[0], option);
						}
						if ( !uint.TryParse(nameValPair[1], out cnv.rgbOutMaxTexSize) || 
									cnv.rgbOutMaxTexSize < Converter.kMinRGBTexSize ||
									cnv.rgbOutMaxTexSize > Converter.kMaxRGBTexSize )
						{
							usage("option {0}: invalid value {1}", nameValPair[0], nameValPair[1] );
						}
						break;
					default:
						usage("unknown option {0}", arg);
						break;
					}
					continue;
				}

				if (arg == "") {
					usage("invalid argument \"\"");
				}

				if (cnv.hmTiffInPath == null) {
					if (Util.IsTiff(arg)) {
						cnv.hmTiffInPath = arg;
						continue;
					}
					usage("Invalid 32 bit float height map input image {0}", arg);
				}

				if (cnv.rgbTiffInPath == null) {
					if (Util.IsTiff(arg) || Util.IsJpg(arg)) {
						cnv.rgbTiffInPath = arg;
						continue;
					}
					usage("Invalid RGB input image {0}", arg);
				}

				if (cnv.outPathBase == null) {
						cnv.outPathBase = arg;
						continue;
				}

				usage("unexpected argument {0}", arg);
			}

			if (	cnv.hmTiffInPath == null ||
						cnv.rgbTiffInPath == null ||
						cnv.outPathBase == null ) 
			{
				usage(args.Length > 0 ? "all inputs and outputs must be specified." : null);
			}

			if (!File.Exists(cnv.hmTiffInPath)) {
				usage("{0} does not exist.", cnv.hmTiffInPath);
			}
			if (!File.Exists(cnv.rgbTiffInPath)) {
				usage("{0} does not exist.", cnv.rgbTiffInPath);
			}

			{
				var outputDir = Path.GetDirectoryName(cnv.outPathBase);
				if (outputDir.Length > 0 && !Directory.Exists(outputDir)) {
					usage("{0} does not exist.", outputDir);
				}
			}

			bool result = cnv.Go();

			Environment.Exit(result ? 0 : 1);
		}

		private static void usage(string message, params object[] args) {
			if (message != null) {
				Console.WriteLine(string.Format(message, args));
			}

			foreach (var ln in usageText) {
				Console.WriteLine(ln);
			}

			Environment.Exit(message == null ? 0 : -1);
		}
	}

	public static class Util {
		public static void Log(string message, params Object[] args) {
			Console.WriteLine(string.Format(message, args));
		}

		public static void Warn(string message, params Object[] args) {
			Console.WriteLine(string.Format(message, args));
		}

		public static void Error(string message, params Object[] args) {
			throw new Exception(string.Format(message, args));
		}

		public static void ByteSwap2(byte[] arr) {
			if ((arr.Length & 0x01) != 0) {
				Util.Error("byteSwap2 array length {0} not a multiple of 2!", arr.Length);
			}
			for (int b = 0; b < arr.Length; b += 2) {
				byte t = arr[b + 0];
				arr[b + 0] = arr[b + 1];
				arr[b + 1] = t;
			}
		}

		public static void ByteSwap4(byte[] arr) {
			if ((arr.Length & 0x03) != 0) {
				Util.Error("byteSwap4 array length {0} not a multiple of 4!", arr.Length);
			}
			for (int b = 0; b < arr.Length; b += 4) {
				byte t = arr[b + 0];
				arr[b + 0] = arr[b + 3];
				arr[b + 3] = t;
				t = arr[b + 1];
				arr[b + 1] = arr[b + 2];
				arr[b + 2] = t;
			}
		}

		public static bool IsJpg(string a) {
			switch (a.ToLower().Substring(a.LastIndexOf('.') + 1)) {
			case "jpg":
			case "jpeg":
			case "jfif":
				return true;
			case "png":
				Util.Error("{0}: file format not supported. Use tif or jpg.", a);
				break;
			}
			return false;
		}

		public static bool IsTiff(string a) {
			switch (a.ToLower().Substring(a.LastIndexOf('.') + 1)) {
			case "tif":
			case "tiff":
				return true;
			case "png":
				Util.Error("{0}: file format not supported. Use tif or jpg.", a);
				break;
			}

			return false;
		}

		public static bool IsRaw(string a) {
			switch (a.ToLower().Substring(a.LastIndexOf('.') + 1)) {
			case "raw":
			case "bin":
				return true;
			}
			return false;
		}
	}
}
