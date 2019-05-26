using System;
using System.IO;

namespace GeoTiff2Raw {
	class Program {
		static private string appName = "GeoTiff2Raw";

		static private string[] usageText = {
			appName + " <input.tif> [-overwrite] <output.raw>",
			"  <input.tif>: source image",
			"  -overwrite: if output image exists it will be overwritten. this is an error otherwise.",
			"  <output.raw>: target raw image"
		};

		static void Main(string[] args) {
			Converter cnv = new Converter();
			bool overwriteOuput = false;

			foreach(var arg in args) {
				if ( arg[0] == '-' ) {
					string option = arg.Substring(1).ToLower();
					switch ( option ) {
					case "h":
					case "help":
					case "u":
					case "usage":
						usage(null);
						break;
					case "overwrite":
						overwriteOuput = true;
						break;
					}
					continue;
				}

				if ( cnv.inputTiffPath == null ) {
					cnv.inputTiffPath = arg;
					continue;
				}

				if ( cnv.outputRawPath == null ) {
					cnv.outputRawPath = arg;
					continue;
				}
			}

			if ( !(cnv.inputTiffPath?.Length > 0) || !(cnv.outputRawPath?.Length > 0) ) {
				usage(args.Length > 0 ? "input and output must be specified." : null);
			}

			if(!File.Exists(cnv.inputTiffPath)) {
				usage("{0} does not exist.", cnv.inputTiffPath);
			}

			{
				var outputDir = Path.GetDirectoryName(cnv.outputRawPath);
				if (outputDir.Length > 0 && !Directory.Exists(outputDir)) {
					usage("{0} does not exist.", outputDir);
				}
			}

			if (!overwriteOuput && File.Exists(cnv.outputRawPath)) {
				usage("{0} exists. use different output path or specify -overwrite.", cnv.outputRawPath);
			}

			bool result = cnv.Go();

			Environment.Exit(result ? 0 : 1);
		}

		private static void usage(string message, params object[] args) {
			if (message != null) {
				Console.WriteLine( string.Format(message, args));
			}

			foreach (var ln in usageText) {
				Console.WriteLine(ln);
			}

			Environment.Exit(message == null ? 0 : -1);
		}
	}
}
