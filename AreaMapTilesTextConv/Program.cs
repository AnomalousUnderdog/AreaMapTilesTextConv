using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;

namespace AreaMapTilesTextConv
{
	internal static class Program
	{
		public static int Main(string[] args)
		{
			if (args.Length == 0)
			{
				// need at least 1 argument, specifying the AreaMapTiles file
				Console.Out.WriteLine("Error: Need at least 1 argument, specifying the AreaMapTiles file");
				return ERROR_INVALID_DATA;
			}

			if (args.Length == 7)
			{
				// 0: path
				// 1: old-file
				// 2: old-hex
				// 3: old-mode
				// 4: new-file
				// 5: new-hex
				// 6: new-mode

				string path = args[0];

				// the AreaMap.txt file would normally be in the same path as the AreaMapTiles file
				string areaMapTxtFile;
				string areaMapFolder = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(areaMapFolder))
				{
					// e.g. "MyMap1.AreaMapTiles.bytes" becomes "MyMap1.AreaMap.txt"
					string areaMapName = Path.GetFileName(path).Replace("Tiles.bytes", ".txt");
					areaMapTxtFile = Path.Combine(areaMapFolder, areaMapName);
				}
				else
				{
					areaMapTxtFile = null;
				}

				string oldFile = args[1];
				string newFile = args[4];

				string savePath = Path.GetTempPath();
				if (!Directory.Exists(savePath))
				{
					savePath = AppDomain.CurrentDomain.BaseDirectory;
				}

				string oldTextFile = Path.Combine(savePath, "OldAreaMapTiles.txt").Replace(@"\", "/");
				string newTextFile = Path.Combine(savePath, "NewAreaMapTiles.txt").Replace(@"\", "/");

				using (var outputFile = new StreamWriter(oldTextFile))
				{
					LoadSingleFile(outputFile, oldFile, areaMapTxtFile);
				}

				using (var outputFile = new StreamWriter(newTextFile))
				{
					LoadSingleFile(outputFile, newFile, areaMapTxtFile);
				}

				var process = new Process();
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.FileName = "git.exe";
				process.StartInfo.Arguments = $"diff --no-index \"{oldTextFile}\" \"{newTextFile}\"";
				process.Start();

				Console.WriteLine(process.StandardOutput.ReadToEnd());
				process.WaitForExit();

				return ERROR_SUCCESS;
			}
			else if (args.Length == 2)
			{
				string areaMapTilesFile = args[0];
				string areaMapTxtFile = args[1];
				return LoadSingleFile(Console.Out, areaMapTilesFile, areaMapTxtFile);
			}
			else
			{
				string filename = args[0];
				return LoadSingleFile(Console.Out, filename);
			}
		}

		// ===============================================================================================

		static int LoadSingleFile(TextWriter output, string filename, string areaMapTxtFilename = null)
		{
			if (!File.Exists(filename))
			{
				output.WriteLine($"Error: File does not exist: \"{filename}\"");
				return ERROR_FILE_NOT_FOUND;
			}

			byte[] bytes = File.ReadAllBytes(filename);

			if (bytes.AllNull())
			{
				output.WriteLine($"Error: File \"{filename}\" has no data inside. Skipping it.");
				return ERROR_INVALID_DATA;
			}

			int errorType;
			string errorMessage;
			using (var stream = new MemoryStream(bytes))
			{
				using (var reader = new BinaryReader(stream))
				{
					(errorType, errorMessage) = LoadFromStream(reader);
				}
			}

			if (errorType == ERROR_SUCCESS)
			{
				string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json").Replace(@"\", "/");

				bool tileTypeFolderExists;
				string tileTypeFolder;
				if (File.Exists(configFilePath))
				{
					string configFileText = File.ReadAllText(configFilePath);

					if (!string.IsNullOrWhiteSpace(configFileText))
					{
						JObject config;
						try
						{
							config = JObject.Parse(configFileText);
						}
						catch (Exception e)
						{
							config = null;
							output.WriteLine($"Cannot determine names of custom Tile Types because of error while parsing config.json:\n{configFilePath}\n{e}");
						}

						if (config != null)
						{
							if (config["TileTypeFolderPath"] != null)
							{
								tileTypeFolder = config["TileTypeFolderPath"].ToString();

								tileTypeFolderExists =
									!string.IsNullOrEmpty(tileTypeFolder) && Directory.Exists(tileTypeFolder);
								if (!tileTypeFolderExists)
								{
									output.WriteLine(
										$"Cannot determine names of custom Tile Types because folder does not exist:\n{tileTypeFolder}");
								}
							}
							else
							{
								output.WriteLine(
									$"Cannot determine names of custom Tile Types because config.json does not have TileTypeFolderPath:\n{configFilePath}");
								tileTypeFolder = null;
								tileTypeFolderExists = false;
							}
						}
						else
						{
							tileTypeFolder = null;
							tileTypeFolderExists = false;
						}
					}
					else
					{
						output.WriteLine($"Cannot determine names of custom Tile Types because config.json is empty:\n{configFilePath}");
						tileTypeFolder = null;
						tileTypeFolderExists = false;
					}
				}
				else
				{
					output.WriteLine($"Cannot determine names of custom Tile Types because config.json does not exist:\n{configFilePath}");
					tileTypeFolder = null;
					tileTypeFolderExists = false;
				}

				// ===============================================================================
				// Getting TileType Names

				TileTypeName.Clear();
				for (int n = 0; n < TileTypeTable.Count; ++n)
				{
					if (TileTypeTable[n] == DEFAULT_WALKABLE_TILE_TYPE_UID)
					{
						TileTypeName.Add(DEFAULT_WALKABLE_TILE_TYPE_NAME);
					}
					else if (TileTypeTable[n] == DEFAULT_WALL_TILE_TYPE_UID)
					{
						TileTypeName.Add(DEFAULT_WALL_TILE_TYPE_NAME);
					}
					else if (tileTypeFolderExists)
					{
						string tileTypeFile = Path.Combine(tileTypeFolder, $"{TileTypeTable[n]}.txt");

						if (!File.Exists(tileTypeFile))
						{
							TileTypeName.Add("File not found");
							continue;
						}

						string tileTypeFileText = File.ReadAllText(tileTypeFile);
						if (string.IsNullOrWhiteSpace(tileTypeFileText))
						{
							TileTypeName.Add("File empty");
							continue;
						}

						try
						{
							var tileType = JObject.Parse(tileTypeFileText);
							if (tileType["DisplayName"] == null)
							{
								TileTypeName.Add("Tile has no DisplayName property");
								continue;
							}

							string tileTypeName = tileType["DisplayName"].ToString();
							if (string.IsNullOrWhiteSpace(tileTypeName))
							{
								TileTypeName.Add("Null");
							}
							else
							{
								TileTypeName.Add(tileTypeName);
							}
						}
						catch (Exception)
						{
							TileTypeName.Add("Error parsing");
						}
					}
					else
					{
						TileTypeName.Add("Unknown Name");
					}
				}

				// ===============================================================================
				// Getting AreaMap.txt file (used to get Grid data, ExitPoint and StartingPosition Names)

				JObject areaMapInfo = null;
				if (!string.IsNullOrWhiteSpace(areaMapTxtFilename) && File.Exists(areaMapTxtFilename))
				{
					string areaMapInfoText = File.ReadAllText(areaMapTxtFilename);
					areaMapInfo = JObject.Parse(areaMapInfoText);
				}
				else
				{
					// the AreaMap.txt file would normally be in the same path as the AreaMapTiles file
					string areaMapFolder = Path.GetDirectoryName(filename);
					if (!string.IsNullOrEmpty(areaMapFolder))
					{
						string areaMapName = Path.GetFileName(filename).Replace("Tiles.bytes", ".txt");
						string areaMapInfoFile = Path.Combine(areaMapFolder, areaMapName);

						if (File.Exists(areaMapInfoFile))
						{
							string areaMapInfoText = File.ReadAllText(areaMapInfoFile);

							areaMapInfo = JObject.Parse(areaMapInfoText);
						}
					}
				}

				// ===============================================================================
				// Finding Width and Height

				int width;
				int height;
				bool gotWidth = false;
				bool gotHeight = false;
				bool gotOffsetLeft = false;
				bool gotOffsetBottom = false;
				if (Width > 0 && Height > 0)
				{
					// newer versions of the AreaMapTiles file already supply the width and height for us
					// so just use that directly
					width = Width;
					height = Height;
				}
				else
				{
					// older versions of the AreaMapTiles file do not supply the width and height information
					// so we have to look for it ourselves

					width = 0;
					height = 0;

					// usually the width and height is in the AreaMap.txt file, encoded in JSON
					var grid = areaMapInfo?["Grid"];
					if (grid != null)
					{
						gotWidth = int.TryParse(grid["Width"]?.ToString(), out width);
						gotHeight = int.TryParse(grid["Height"]?.ToString(), out height);

						// the offsets are also present here so we might as well get them
						gotOffsetLeft = int.TryParse(grid["LeftEdge"]?.ToString(), out OffsetLeft);
						gotOffsetBottom = int.TryParse(grid["BottomEdge"]?.ToString(), out OffsetBottom);
					}

					if (!gotWidth || !gotHeight)
					{
						// did not get any width/height information
						// (maybe AreaMap.txt file is not present)
						//
						// so find a suitable width that won't have "leftover" tiles
						width = 50;
						if (TileTypes.Count >= 50)
						{
							while (TileTypes.Count % width != 0)
							{
								++width;
							}
						}
						else
						{
							while (TileTypes.Count % width != 0)
							{
								--width;
							}
						}
						height = TileTypes.Count / width;
					}
				}

				// ===============================================================================
				// Getting ExitPoint and StartingPosition Names

				ExitPointName.Clear();
				var exitPoints = areaMapInfo?["ExitPoints"];
				if (exitPoints != null)
				{
					for (int n = 0; n < ExitPointTable.Count; ++n)
					{
						ExitPointName.Add(string.Empty);
					}
					for (int n = 0; n < ExitPointTable.Count; ++n)
					{
						int exitPointNumber = 0;
						foreach (var exitPoint in exitPoints)
						{
							++exitPointNumber;
							if (ExitPointTable[n] == exitPoint["UniqueID"].ToString())
							{
								string inGameLabel = exitPoint["InGameLabel"].ToString();
								string editorName = exitPoint["EditorName"].ToString();
								if (!string.IsNullOrWhiteSpace(inGameLabel) && !string.IsNullOrWhiteSpace(editorName))
								{
									ExitPointName[n] = $"Exit Point {exitPointNumber}: {inGameLabel} ({editorName})";
								}
								else if (!string.IsNullOrWhiteSpace(inGameLabel))
								{
									ExitPointName[n] = $"Exit Point {exitPointNumber}: {inGameLabel}";
								}
								else if (!string.IsNullOrWhiteSpace(editorName))
								{
									ExitPointName[n] = $"Exit Point {exitPointNumber}: {editorName}";
								}
								else
								{
									ExitPointName[n] = $"Exit Point {exitPointNumber}";
								}
								break;
							}
						}
					}
				}

				StartingPositionName.Clear();
				var startingPositions = areaMapInfo?["StartingPositions"];
				if (startingPositions != null)
				{
					for (int n = 0; n < StartingPositionTable.Count; ++n)
					{
						StartingPositionName.Add(string.Empty);
					}
					for (int n = 0; n < StartingPositionTable.Count; ++n)
					{
						int startingPositionNumber = 0;
						foreach (var startingPosition in startingPositions)
						{
							++startingPositionNumber;
							if (StartingPositionTable[n] == startingPosition["UniqueID"].ToString())
							{
								string name = startingPosition["Name"].ToString();
								if (!string.IsNullOrWhiteSpace(name))
								{
									StartingPositionName[n] = $"Starting Position {startingPositionNumber}: {name}";
								}
								else
								{
									StartingPositionName[n] = $"Starting Position {startingPositionNumber}";
								}
								break;
							}
						}
					}
				}

				// ===============================================================================

				const string DIVIDER = "=-=-=-=-============================================-=-=-=-=\n";

				void WriteRowHeader()
				{
					output.Write("  ");
					for (int currentX = 0; currentX < width; currentX++)
					{
						output.Write(currentX % 10);
					}

					output.Write("\n");
				}

				output.Write(DIVIDER);
				output.WriteLine($"Version = {Version}");
				if (Version <= 1)
				{
					if (gotWidth)
					{
						output.WriteLine($"Width = {width}");
					}
					else
					{
						output.WriteLine($"Width = {width}?");
					}

					if (gotHeight)
					{
						output.WriteLine($"Height = {height}");
					}
					else
					{
						output.WriteLine($"Height = {height}?");
					}

					if (gotOffsetLeft)
					{
						output.WriteLine($"OffsetLeft = {OffsetLeft}");
					}
					else
					{
						output.WriteLine($"OffsetLeft = {OffsetLeft}?");
					}

					if (gotOffsetBottom)
					{
						output.WriteLine($"OffsetBottom = {OffsetBottom}");
					}
					else
					{
						output.WriteLine($"OffsetBottom = {OffsetBottom}?");
					}
				}
				else
				{
					output.WriteLine($"Width = {width}");
					output.WriteLine($"Height = {height}");
					output.WriteLine($"OffsetLeft = {OffsetLeft}");
					output.WriteLine($"OffsetBottom = {OffsetBottom}");
				}
				output.Write(DIVIDER);

				// ===============================================================================
				// TileTypes

				for (int n = 0; n < TileTypeTable.Count; ++n)
				{
					if (n < TileTypeName.Count)
					{
						output.WriteLine($"{TileTypeSymbol[n]} = {TileTypeTable[n]} = {TileTypeName[n]}");
					}
					else
					{
						output.WriteLine($"{TileTypeSymbol[n]} = {TileTypeTable[n]}");
					}
				}
				output.Write(DIVIDER);

				WriteRowHeader();

				for (int currentY = height-1; currentY >= 0; --currentY)
				{
					output.Write(currentY % 10);
					output.Write(":");
					for (int currentX = 0; currentX < width; currentX++)
					{
						byte idx = TileTypes[GetIndexNoOffset(currentX, currentY, width)];
						output.Write(TileTypeSymbol[idx]);
					}
					output.Write("\n");
				}
				output.Write(DIVIDER);

				// ===============================================================================
				// Exit Points

				for (int n = 0; n < ExitPointTable.Count; ++n)
				{
					if (n == 0)
					{
						output.WriteLine($"{TileTypeSymbol[n]} = No Exit Point");
					}
					else if (n < ExitPointName.Count)
					{
						output.WriteLine($"{TileTypeSymbol[n]} = {ExitPointTable[n]} = {ExitPointName[n]}");
					}
					else
					{
						output.WriteLine($"{TileTypeSymbol[n]} = {ExitPointTable[n]}");
					}
				}
				output.Write(DIVIDER);

				WriteRowHeader();

				for (int currentY = height-1; currentY >= 0; --currentY)
				{
					output.Write(currentY % 10);
					output.Write(":");
					for (int currentX = 0; currentX < width; currentX++)
					{
						byte idx = ExitPoints[GetIndexNoOffset(currentX, currentY, width)];
						output.Write(TileTypeSymbol[idx]);
					}
					output.Write("\n");
				}
				output.Write(DIVIDER);

				// ===============================================================================
				// Starting Positions

				for (int n = 0; n < StartingPositionTable.Count; ++n)
				{
					if (n == 0)
					{
						output.WriteLine($"{TileTypeSymbol[n]} = No Starting Position");
					}
					else if (n < StartingPositionName.Count)
					{
						output.WriteLine($"{TileTypeSymbol[n]} = {StartingPositionTable[n]} = {StartingPositionName[n]}");
					}
					else
					{
						output.WriteLine($"{TileTypeSymbol[n]} = {StartingPositionTable[n]}");
					}
				}
				output.Write(DIVIDER);

				WriteRowHeader();

				for (int currentY = height-1; currentY >= 0; --currentY)
				{
					output.Write(currentY % 10);
					output.Write(":");
					for (int currentX = 0; currentX < width; currentX++)
					{
						byte idx = StartingPositions[GetIndexNoOffset(currentX, currentY, width)];
						output.Write(TileTypeSymbol[idx]);
					}
					output.Write("\n");
				}
				output.Write(DIVIDER);

				for (int currentY = height-1; currentY >= 0; --currentY)
				{
					output.Write(currentY % 10);
					output.Write(":");
					for (int currentX = 0; currentX < width; currentX++)
					{
						int i = GetIndexNoOffset(currentX, currentY, width);
						if (StartingPositions[i] == 0)
						{
							output.Write(' ');
						}
						else
						{
							byte idx = StartingPositionFacings[i];
							output.Write(FacingSymbol[idx]);
						}
					}
					output.Write("\n");
				}
				output.Write(DIVIDER);

				//
				// ===============================================================================

				return ERROR_SUCCESS;
			}
			else
			{
				output.WriteLine($"Error: {errorMessage}");
				return errorType;
			}
		}

		// ===============================================================================================

		static readonly char[] TileTypeSymbol =
		{
			'.', 'X', '#', '%', '*', '-',
			'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
			'1', '2', '3', '4', '5', '6', '7', '8', '9',
			'@', '$', '^', '~', '&', '+', '?',
			'█', '░', '▒', '▓',
			'<', '>', '(', ')', '[', ']', '{', '}', ',', '/', '\\', '`', '"',
		};

		static readonly char[] FacingSymbol =
		{
			'N', 'n', 'E', 'e', 'S', 's', 'W', 'w'
		};

		static readonly char[] FacingArrowSymbol =
		{
			'↑', '↗', '→', '↘', '↓', '↙', '←', '↖'
		};

		// ===============================================================================================

		// from https://docs.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499-
		const int ERROR_SUCCESS = 0;
		const int ERROR_FILE_NOT_FOUND = 2;
		const int ERROR_INVALID_DATA = 13;

		const string DEFAULT_WALKABLE_TILE_TYPE_UID = "COFFEEWALK8899.AreaMapTile";
		const string DEFAULT_WALL_TILE_TYPE_UID = "DEADBEEFIN6969.AreaMapTile";

		const string DEFAULT_WALKABLE_TILE_TYPE_NAME = "Default Walkable Tile";
		const string DEFAULT_WALL_TILE_TYPE_NAME = "Default Wall Tile";

		/// <summary>
		/// </summary>
		/// <remarks>
		/// <para>Version 1: Initial format.</para>
		/// <para>Version 2: Added two ints for width and height of map, and two ints for the x,y position of the map.</para>
		/// </remarks>
		const int LAST_KNOWN_HIGHEST_VERSION = 2;

		const string FILE_ID = "AreaMapTiles";

		// ===============================================================================================

		static int Version;

		static int Width;

		static int Height;

		/// <summary>
		/// Where the Left Edge of the map is, relative to the world's origin.
		/// </summary>
		static int OffsetLeft;

		/// <summary>
		/// Where the Bottom Edge of the map is, relative to the world's origin.
		/// </summary>
		static int OffsetBottom;

		static readonly List<string> TileTypeName = new List<string>();
		static readonly List<string> ExitPointName = new List<string>();
		static readonly List<string> StartingPositionName = new List<string>();

		static readonly List<string> TileTypeTable = new List<string>();
		static readonly List<byte> TileTypes = new List<byte>();
		static readonly List<string> ExitPointTable = new List<string>();
		static readonly List<byte> ExitPoints = new List<byte>();
		static readonly List<string> StartingPositionTable = new List<string>();
		static readonly List<byte> StartingPositions = new List<byte>();
		static readonly List<byte> StartingPositionFacings = new List<byte>();

		// ===============================================================================================

		static (int errorType, string errorMessage) LoadFromStream(BinaryReader reader)
		{
			string fileType = reader.ReadString();
			if (fileType != FILE_ID)
			{
				return (ERROR_INVALID_DATA, $"Wrong File ID (got: {fileType}, needs: {FILE_ID})");
			}

			Version = reader.ReadInt32();

			// -----------------------------------

			if (Version >= 2)
			{
				Width = reader.ReadInt32();
				Height = reader.ReadInt32();
				OffsetLeft = reader.ReadInt32();
				OffsetBottom = reader.ReadInt32();
			}
			else
			{
				// Version 1 doesn't have this,
				// it will rely on DLD.GWP.Map.AreaMap (which implements IGridBoundary)
				Width = 0;
				Height = 0;
				OffsetLeft = 0;
				OffsetBottom = 0;
			}


			// -----------------------------------

			int tileTypeTableLen = reader.ReadInt32();
			TileTypeTable.Clear();
			for (int n = 0; n < tileTypeTableLen; ++n)
			{
				string gotTileTypeUid = reader.ReadString();
				TileTypeTable.Add(!string.IsNullOrWhiteSpace(gotTileTypeUid) ? gotTileTypeUid : null);
			}

			int tileTypeLen = reader.ReadInt32();
			TileTypes.Clear();
			for (int n = 0; n < tileTypeLen; ++n)
			{
				TileTypes.Add(reader.ReadByte());
			}

			// -----------------------------------

			int exitPointTableLen = reader.ReadInt32();
			ExitPointTable.Clear();
			for (int n = 0; n < exitPointTableLen; ++n)
			{
				string gotExitPointUid = reader.ReadString();
				ExitPointTable.Add(!string.IsNullOrWhiteSpace(gotExitPointUid) ? gotExitPointUid : null);
			}

			int exitPointLen = reader.ReadInt32();
			ExitPoints.Clear();
			for (int n = 0; n < exitPointLen; ++n)
			{
				ExitPoints.Add(reader.ReadByte());
			}

			// -----------------------------------

			int startingPositionTableLen = reader.ReadInt32();
			StartingPositionTable.Clear();
			for (int n = 0; n < startingPositionTableLen; ++n)
			{
				string gotStartingPositionUid = reader.ReadString();
				StartingPositionTable.Add(
					!string.IsNullOrWhiteSpace(gotStartingPositionUid) ? gotStartingPositionUid : null);
			}

			int startingPositionLen = reader.ReadInt32();
			StartingPositions.Clear();
			for (int n = 0; n < startingPositionLen; ++n)
			{
				StartingPositions.Add(reader.ReadByte());
			}

			int startingPositionFacingLen = reader.ReadInt32();
			StartingPositionFacings.Clear();
			for (int n = 0; n < startingPositionFacingLen; ++n)
			{
				StartingPositionFacings.Add(reader.ReadByte());
			}

			// -----------------------------------

			return (ERROR_SUCCESS, null);
		}

		/// <summary>
		/// Expects x, and y to be in local space (meaning 0, 0 is guaranteed to be bottom-left corner of the map).
		/// </summary>
		/// <param name="x"></param>
		/// <param name="y"></param>
		/// <param name="w">Width</param>
		/// <returns></returns>
		static int GetIndexNoOffset(int x, int y, int w)
		{
			return (y * w) + x;
		}
	}
}