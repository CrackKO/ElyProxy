using System;
using System.Globalization;

namespace ElyProxy.Services;

public static class TrafficFormatter
{
	private static readonly string[] Units = new string[5] { "Б", "КБ", "МБ", "ГБ", "ТБ" };

	public static string FormatBytes(long bytes)
	{
		if (bytes <= 0)
		{
			return "0 Б";
		}
		double value = bytes;
		int unit = 0;
		while (value >= 1024 && unit < Units.Length - 1)
		{
			value /= 1024;
			unit++;
		}
		string format = (value >= 100 || unit == 0) ? "0" : "0.#";
		return value.ToString(format, CultureInfo.InvariantCulture) + " " + Units[unit];
	}

	public static string FormatSpeed(double bytesPerSecond)
	{
		return FormatBytes((long)Math.Max(0, bytesPerSecond)) + "/с";
	}
}
