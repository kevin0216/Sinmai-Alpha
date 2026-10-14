// Scan-based reference from source commit cd15585, before the MaiMagic sync.
using System;
using System.Collections.Generic;
namespace SinmaiAlpha.ChartVisuals
{
internal sealed class ReferenceNoiseZoneTimeline
{
	private readonly List<NoiseZoneEvent> events = new List<NoiseZoneEvent>();

	private readonly List<NoiseZoneEvent>[] spans = new List<NoiseZoneEvent>[33];

	public void Configure(IEnumerable<NoiseZoneEvent> source)
	{
		events.Clear();
		if (source != null)
		{
			events.AddRange(source);
		}
		events.Sort(delegate(NoiseZoneEvent a, NoiseZoneEvent b)
		{
			double warningStart = a.WarningStart;
			return warningStart.CompareTo(b.WarningStart);
		});
		for (int num = 0; num < spans.Length; num++)
		{
			List<NoiseZoneEvent>[] array = spans;
			int num2 = num;
			if (array[num2] == null)
			{
				array[num2] = new List<NoiseZoneEvent>();
			}
			spans[num].Clear();
			List<NoiseZoneEvent> list = new List<NoiseZoneEvent>();
			foreach (NoiseZoneEvent @event in events)
			{
				if (@event.Sensor == num)
				{
					list.Add(@event);
				}
			}
			list.Sort(delegate(NoiseZoneEvent a, NoiseZoneEvent b)
			{
				double start = a.Start;
				return start.CompareTo(b.Start);
			});
			foreach (NoiseZoneEvent item in list)
			{
				if (spans[num].Count == 0 || spans[num][spans[num].Count - 1].End < item.Start)
				{
					spans[num].Add(item);
					continue;
				}
				NoiseZoneEvent noiseZoneEvent = spans[num][spans[num].Count - 1];
				spans[num][spans[num].Count - 1] = new NoiseZoneEvent(num, noiseZoneEvent.Start, Math.Max(noiseZoneEvent.End, item.End) - noiseZoneEvent.Start, noiseZoneEvent.Start - noiseZoneEvent.WarningStart);
			}
		}
	}

	public bool IsBlocked(int sensor, double time)
	{
		foreach (NoiseZoneEvent @event in events)
		{
			if (@event.WarningStart > time)
			{
				break;
			}
			if (@event.Sensor == sensor && time >= @event.Start && time < @event.End)
			{
				return true;
			}
		}
		return false;
	}

	public void Evaluate(double time, float[] warnings, bool[] active)
	{
		Array.Clear(warnings, 0, warnings.Length);
		Array.Clear(active, 0, active.Length);
		foreach (NoiseZoneEvent @event in events)
		{
			if (@event.WarningStart > time)
			{
				break;
			}
			if (@event.Sensor >= 0 && @event.Sensor < active.Length && !(time >= @event.End))
			{
				if (time >= @event.Start)
				{
					active[@event.Sensor] = true;
					continue;
				}
				double num = (time - @event.WarningStart) / (@event.Start - @event.WarningStart);
				warnings[@event.Sensor] = Math.Max(warnings[@event.Sensor], (float)(1.0 - 0.7 * num));
			}
		}
	}

	public bool TryGetActiveSpan(int sensor, double time, out double start, out double end)
	{
		start = (end = 0.0);
		if (sensor < 0 || sensor >= spans.Length || spans[sensor] == null)
		{
			return false;
		}
		List<NoiseZoneEvent> list = spans[sensor];
		int num = 0;
		int num2 = list.Count - 1;
		int num3 = -1;
		while (num <= num2)
		{
			int num4 = (num + num2) / 2;
			if (list[num4].Start <= time)
			{
				num3 = num4;
				num = num4 + 1;
			}
			else
			{
				num2 = num4 - 1;
			}
		}
		if (num3 >= 0 && time < list[num3].End)
		{
			start = list[num3].Start;
			end = list[num3].End;
			return true;
		}
		return false;
	}

	public bool TryGetVisualSpan(int sensor, double time, out double start, out double end)
	{
		start = (end = 0.0);
		if (sensor < 0 || sensor >= spans.Length || spans[sensor] == null)
		{
			return false;
		}
		List<NoiseZoneEvent> list = spans[sensor];
		int num = 0;
		int num2 = list.Count - 1;
		int num3 = -1;
		while (num <= num2)
		{
			int num4 = (num + num2) / 2;
			if (list[num4].Start <= time)
			{
				num3 = num4;
				num = num4 + 1;
			}
			else
			{
				num2 = num4 - 1;
			}
		}
		if (num3 < 0)
		{
			return false;
		}
		NoiseZoneEvent noiseZoneEvent = list[num3];
		double num5 = Math.Max(noiseZoneEvent.End, noiseZoneEvent.Start + 0.1);
		if (time >= num5)
		{
			return false;
		}
		start = noiseZoneEvent.Start;
		end = num5;
		return true;
	}

	public float WarningShine(int sensor, double time)
	{
		if (IsBlocked(sensor, time))
		{
			return 0f;
		}
		foreach (NoiseZoneEvent @event in events)
		{
			if (@event.WarningStart > time)
			{
				break;
			}
			if (@event.Sensor == sensor && time >= Math.Max(@event.WarningStart, @event.Start - 0.5) && time < @event.Start)
			{
				return (float)(0.12 * (1.0 + 0.5 * Math.Sin(time * 37.9)));
			}
		}
		return 0f;
	}

	public static int SensorIndex(char area, int position)
	{
		switch (char.ToUpperInvariant(area))
		{
		case 'A':
			if (position >= 1 && position <= 8)
			{
				return position - 1;
			}
			break;
		case 'B':
			if (position >= 1 && position <= 8)
			{
				return position + 7;
			}
			break;
		case 'C':
			return 16;
		case 'D':
			if (position >= 1 && position <= 8)
			{
				return position + 16;
			}
			break;
		case 'E':
			if (position >= 1 && position <= 8)
			{
				return position + 24;
			}
			break;
		}
		return -1;
	}
}
}
