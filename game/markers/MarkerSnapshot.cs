using System;
using Godot;

public readonly struct ShotMarkerData : IEquatable<ShotMarkerData>
{
	public static readonly ShotMarkerData Hidden = new(false, Vector3.Zero, string.Empty, 0);

	public ShotMarkerData(bool visible, Vector3 worldPoint, string distanceText, int elevationFeet)
	{
		Visible = visible;
		WorldPoint = worldPoint;
		DistanceText = distanceText ?? string.Empty;
		ElevationFeet = elevationFeet;
	}

	public bool Visible { get; }
	public Vector3 WorldPoint { get; }
	public string DistanceText { get; }
	public int ElevationFeet { get; }

	public bool Equals(ShotMarkerData other)
	{
		return Visible == other.Visible
			&& WorldPoint == other.WorldPoint
			&& DistanceText == other.DistanceText
			&& ElevationFeet == other.ElevationFeet;
	}

	public override bool Equals(object obj)
	{
		return obj is ShotMarkerData other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Visible, WorldPoint, DistanceText, ElevationFeet);
	}

	public static bool operator ==(ShotMarkerData left, ShotMarkerData right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(ShotMarkerData left, ShotMarkerData right)
	{
		return !left.Equals(right);
	}
}

public readonly struct MarkerSnapshot : IEquatable<MarkerSnapshot>
{
	public static readonly MarkerSnapshot Hidden = new(ShotMarkerData.Hidden, ShotMarkerData.Hidden);

	public MarkerSnapshot(ShotMarkerData flag, ShotMarkerData player)
	{
		Flag = flag;
		Player = player;
	}

	public ShotMarkerData Flag { get; }
	public ShotMarkerData Player { get; }

	public bool Equals(MarkerSnapshot other)
	{
		return Flag.Equals(other.Flag) && Player.Equals(other.Player);
	}

	public override bool Equals(object obj)
	{
		return obj is MarkerSnapshot other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Flag, Player);
	}

	public static bool operator ==(MarkerSnapshot left, MarkerSnapshot right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(MarkerSnapshot left, MarkerSnapshot right)
	{
		return !left.Equals(right);
	}
}
