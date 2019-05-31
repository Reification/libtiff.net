using System;
using System.IO;

namespace GeoTiff2Unity {
	class Program {
		static private string appName = "GeoTiff2Raw";

		static private string[] usageText = {
			appName + " <inputHeight.tif> <inputRGB.tif> [-overwrite] <outputHeight.raw> <outputRGB.tif>",
			"  <inputHeight.tif>: source 32 bit float height map image",
			"  <inputRGB.tif>: source RGB texture matching height map",
			"  -overwrite: if output image exists it will be overwritten. this is an error otherwise.",
			"  <outputHeight.raw>: target raw heightmap for import into Unity",
			"  <outputRGB.tif>: target RGB texture for import into Unity"
		};

		static bool isJpg(string a) {
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

		static bool isTiff(string a) {
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

		static bool isRaw(string a) {
			switch (a.ToLower().Substring(a.LastIndexOf('.') + 1)) {
			case "raw":
			case "bin":
				return true;
			}
			return false;
		}

		static void Main(string[] args) {
			Converter cnv = new Converter();
			bool overwriteOuput = false;

			foreach (var arg in args) {
				if (arg[0] == '-') {
					string option = arg.Substring(1).ToLower();
					switch (option) {
					case "h":
					case "help":
					case "u":
					case "usage":
						usage(null);
						break;
					case "overwrite":
						overwriteOuput = true;
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

				if (cnv.inputFloatHeightTifPath == null) {
					if (isTiff(arg)) {
						cnv.inputFloatHeightTifPath = arg;
						continue;
					}
					usage("Invalid 32 bit float height map input image {0}", arg);
				}

				if (cnv.inputRGBTifPath == null) {
					if (isTiff(arg) || isJpg(arg)) {
						cnv.inputRGBTifPath = arg;
						continue;
					}
					usage("Invalid RGB input image {0}", arg);
				}

				if (cnv.outputRawHeightPath == null) {
					if (isRaw(arg)) {
						cnv.outputRawHeightPath = arg;
						continue;
					}
					usage("Invalid raw height map output image {0}", arg);
				}

				if (cnv.outputRGBTifPath == null) {
					if (isTiff(arg) || isJpg(arg)) {
						cnv.outputRGBTifPath = arg;
						continue;
					}
					usage("Invalid RGB output image {0}", arg);
				}

				usage("unexpected argument {0}", arg);
			}

			if (	cnv.inputFloatHeightTifPath == null ||
						cnv.inputRGBTifPath == null ||
						cnv.outputRawHeightPath == null ||
						cnv.outputRGBTifPath == null ) 
			{
				usage(args.Length > 0 ? "all inputs and outputs must be specified." : null);
			}

			if (!File.Exists(cnv.inputFloatHeightTifPath)) {
				usage("{0} does not exist.", cnv.inputFloatHeightTifPath);
			}
			if (!File.Exists(cnv.inputRGBTifPath)) {
				usage("{0} does not exist.", cnv.inputRGBTifPath);
			}

			{
				var outputDir = Path.GetDirectoryName(cnv.outputRawHeightPath);
				if (outputDir.Length > 0 && !Directory.Exists(outputDir)) {
					usage("{0} does not exist.", outputDir);
				}
			}
			{
				var outputDir = Path.GetDirectoryName(cnv.outputRGBTifPath);
				if (outputDir.Length > 0 && !Directory.Exists(outputDir)) {
					usage("{0} does not exist.", outputDir);
				}
			}

			if (!overwriteOuput && (File.Exists(cnv.outputRawHeightPath) || File.Exists(cnv.outputRGBTifPath))) {
				usage("{0} and/or {1} exists. use different output paths or specify -overwrite.",
					cnv.outputRawHeightPath,
					cnv.outputRGBTifPath);
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
}
