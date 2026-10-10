using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HorrorGame;

internal static class SaveDataPaths
{
	public static string DirectoryPath { get; } = ResolveDirectory();
	private static string ResolveDirectory([CallerFilePath] string sourceFile = "")
	{
#if DEBUG
		// 実行時の作業ディレクトリに依存せず、対応するソリューションを探す。
		for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "Horror.sln")))
				return Path.Combine(directory.FullName, "SaveData");
		string solutionDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, ".."));
		return Path.Combine(solutionDirectory, "SaveData");
#else
		return AppContext.BaseDirectory;
#endif
	}
}

internal enum EndingId { Dawn, Revenge, Conviction, SilentWitness }

internal sealed class EndingAchievements
{
	public static readonly EndingId[] Endings = Enum.GetValues<EndingId>();
	private readonly HashSet<EndingId> seen = new();
	private readonly string? filePath;
	public string? StorageError { get; private set; }
	public int Count => seen.Count;
	// null は描画テスト用。実際の実績ファイルには読み書きしない。
	public EndingAchievements(string? filePath)
	{
		this.filePath = filePath;
		if (filePath == null) return;
		try
		{
			if (!File.Exists(filePath)) return;
			string[] ids = JsonSerializer.Deserialize<string[]>(File.ReadAllText(filePath)) ?? [];
			foreach (string id in ids)
				if (Enum.TryParse(id, out EndingId ending) && Enum.IsDefined(ending)) seen.Add(ending);
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
		{
			StorageError = "実績データを読み込めませんでした。";
		}
	}
	public static EndingId? FromWorld(World world) => world.Phase != Phase.Escaped ? null
		: world.SilentWitnessEnding ? EndingId.SilentWitness
		: world.ConvictionEnding ? EndingId.Conviction
		: world.RevengeEnding ? EndingId.Revenge : EndingId.Dawn;
	public bool Seen(EndingId ending) => seen.Contains(ending);
	public string Label(EndingId ending) => !Seen(ending) ? "？？？" : ending switch
	{
		EndingId.Dawn => "GOOD END — 夜明け",
		EndingId.Revenge => "BAD END — 血塗られた朝",
		EndingId.Conviction => "BAD END — 誰が見ていた？",
		EndingId.SilentWitness => "BAD END — 消えない目撃者",
		_ => "？？？"
	};
	public void Unlock(EndingId ending)
	{
		if (!seen.Add(ending) && StorageError == null) return;
		if (filePath == null) return;
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
			string json = JsonSerializer.Serialize(Endings.Where(Seen).Select(id => id.ToString()).ToArray(),
				new JsonSerializerOptions { WriteIndented = true });
			// 書き込み中に終了しても以前の記録を壊さないよう、同じ保存先で置き換える。
			File.WriteAllText(filePath + ".tmp", json);
			File.Move(filePath + ".tmp", filePath, true);
			StorageError = null;
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException)
		{
			StorageError = "実績を保存できませんでした。";
		}
	}
}
