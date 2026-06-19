using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace OsuLazerFSMounter.Utility;
public static class Helper
{
	public static bool HasFlag(this uint value, uint flag)
	{
		return (value & flag) == flag;
	}
	public static bool HasFlag(this int value, int flag)
	{
		return (value & flag) == flag;
	}

	[return: NotNull()]
	public static T ThrowIfNull<T>(this T obj)
	{
		if (obj is null)
			throw new ArgumentNullException(nameof(obj), "Object cannot be null.");
		return obj;
	}

	/// <summary>
	/// if b is a prefix of a, returns true, otherwise false
	/// </summary>
	/// <param name="a"></param>
	/// <param name="b"></param>
	/// <returns></returns>
	public static bool HasPrefixOf(this string[] a, string[] b)
	{
		if (a.Length < b.Length)
			return false;

		for (int i = 0; i < b.Length; i++)
		{
			if (a[i] != b[i])
				return false;
		}
		return true;
	}
	public static string[] ReplacePrefix(this string[] a, string[] prefixToReplace, string[] replacement)
	{
		if (!a.HasPrefixOf(prefixToReplace))
			throw new ArgumentException("The array does not have the specified prefix.", nameof(prefixToReplace));

		return replacement.Concat(a.Skip(prefixToReplace.Length)).ToArray();
	}
	public static List<char> GetAvailableDriveLetters()
	{
		char[] usedLetters = DriveInfo.GetDrives().Select(d => d.Name[0]).ToArray();
		List<char> availableLetters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToList();
		availableLetters.RemoveAll(c => usedLetters.Contains(c));

		return availableLetters;
	}
	public static void AppendStream(this IncrementalHash self, Stream stream, int bufferSize = 8192)
	{
		byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

		try
		{
			int bytesRead;
			while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
			{
				self.AppendData(buffer, 0, bytesRead);
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}
}
