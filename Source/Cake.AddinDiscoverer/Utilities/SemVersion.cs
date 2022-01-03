using System;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace Cake.AddinDiscoverer.Utilities
{
	/// <summary>
	/// A semantic version implementation.
	/// Conforms to v2.0.0 of http://semver.org/.
	/// </summary>
	[Serializable]
	internal sealed class SemVersion : IComparable<SemVersion>, IComparable, ISerializable
	{
		private static readonly Regex PARSE_REGEX =
			new Regex(
				@"^(?<major>\d+)" +
				@"(\.(?<minor>\d+))?" +
				@"(\.(?<patch>\d+))?" +
				@"(\-(?<pre>[0-9A-Za-z\-\.]+))?" +
				@"(\+(?<build>[0-9A-Za-z\-\.]+))?$",
				RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.ExplicitCapture);

		/// <summary>
		/// Initializes a new instance of the <see cref="SemVersion" /> class.
		/// </summary>
		/// <param name="major">The major version.</param>
		/// <param name="minor">The minor version.</param>
		/// <param name="patch">The patch version.</param>
		/// <param name="prerelease">The prerelease version (eg. "alpha").</param>
		/// <param name="build">The build eg ("nightly.232").</param>
		public SemVersion(int major, int minor = 0, int patch = 0, string prerelease = "", string build = "")
		{
			this.Major = major;
			this.Minor = minor;
			this.Patch = patch;

			this.Prerelease = prerelease ?? string.Empty;
			this.Build = build ?? string.Empty;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="SemVersion"/> class.
		/// </summary>
		/// <param name="version">The <see cref="System.Version"/> that is used to initialize
		/// the Major, Minor, Patch and Build properties.</param>
		/// <remarks>
		/// Counter-intuitively, the 'Build' property of the Version does not correspond to the 'Build property of the SemVersion.
		/// </remarks>
		public SemVersion(Version version)
		{
			if (version == null)
				throw new ArgumentNullException("version");

			this.Major = version.Major;
			this.Minor = version.Minor;
			this.Patch = version.Build;
			this.Build = version.Revision > 0 ? version.Revision.ToString() : string.Empty;
			this.Prerelease = string.Empty;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="SemVersion" /> class.
		/// </summary>
		/// <param name="info">The serialization info.</param>
		/// <param name="context">The serialization context.</param>
		/// <exception cref="ArgumentNullException">If info is null.</exception>
		private SemVersion(SerializationInfo info, StreamingContext context)
		{
			if (info == null) throw new ArgumentNullException("info");
			var semVersion = Parse(info.GetString("SemVersion"));
			Major = semVersion.Major;
			Minor = semVersion.Minor;
			Patch = semVersion.Patch;
			Prerelease = semVersion.Prerelease;
			Build = semVersion.Build;
		}

		/// <summary>
		/// Parses the specified string to a semantic version.
		/// </summary>
		/// <param name="version">The version string.</param>
		/// <param name="strict">If set to <c>true</c> minor and patch version are required, else they default to 0.</param>
		/// <returns>The SemVersion object.</returns>
		/// <exception cref="System.InvalidOperationException">When a invalid version string is passed.</exception>
		public static SemVersion Parse(string version, bool strict = false)
		{
			var match = PARSE_REGEX.Match(version);
			if (!match.Success)
				throw new ArgumentException("Invalid version.", "version");

			var major = int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture);

			var minorMatch = match.Groups["minor"];
			int minor = 0;
			if (minorMatch.Success)
			{
				minor = int.Parse(minorMatch.Value, CultureInfo.InvariantCulture);
			}
			else if (strict)
			{
				throw new InvalidOperationException("Invalid version (no minor version given in strict mode)");
			}

			var patchMatch = match.Groups["patch"];
			int patch = 0;
			if (patchMatch.Success)
			{
				patch = int.Parse(patchMatch.Value, CultureInfo.InvariantCulture);
			}
			else if (strict)
			{
				throw new InvalidOperationException("Invalid version (no patch version given in strict mode)");
			}

			var prerelease = match.Groups["pre"].Value;
			var build = match.Groups["build"].Value;

			return new SemVersion(major, minor, patch, prerelease, build);
		}

		/// <summary>
		/// Parses the specified string to a semantic version.
		/// </summary>
		/// <param name="version">The version string.</param>
		/// <param name="semver">When the method returns, contains a SemVersion instance equivalent
		/// to the version string passed in, if the version string was valid, or <c>null</c> if the
		/// version string was not valid.</param>
		/// <param name="strict">If set to <c>true</c> minor and patch version are required, else they default to 0.</param>
		/// <returns><c>False</c> when a invalid version string is passed, otherwise <c>true</c>.</returns>
		public static bool TryParse(string version, out SemVersion semver, bool strict = false)
		{
			try
			{
				semver = Parse(version, strict);
				return true;
			}
			catch (Exception)
			{
				semver = null;
				return false;
			}
		}

		/// <summary>
		/// Tests the specified versions for equality.
		/// </summary>
		/// <param name="versionA">The first version.</param>
		/// <param name="versionB">The second version.</param>
		/// <returns>If versionA is equal to versionB <c>true</c>, else <c>false</c>.</returns>
		public static bool Equals(SemVersion versionA, SemVersion versionB)
		{
			if (versionA is null) return versionB is null;
			return versionA.Equals(versionB);
		}

		/// <summary>
		/// Compares the specified versions.
		/// </summary>
		/// <param name="versionA">The version to compare to.</param>
		/// <param name="versionB">The version to compare against.</param>
		/// <returns>If versionA &lt; versionB <c>&lt; 0</c>, if versionA &gt; versionB <c>&gt; 0</c>,
		/// if versionA is equal to versionB <c>0</c>.</returns>
		public static int Compare(SemVersion versionA, SemVersion versionB)
		{
			if (versionA is null) return versionB is null ? 0 : -1;
			return versionA.CompareTo(versionB);
		}

		/// <summary>
		/// Gets the major version.
		/// </summary>
		/// <value>
		/// The major version.
		/// </value>
		public int Major { get; private set; }

		/// <summary>
		/// Gets the minor version.
		/// </summary>
		/// <value>
		/// The minor version.
		/// </value>
		public int Minor { get; private set; }

		/// <summary>
		/// Gets the patch version.
		/// </summary>
		/// <value>
		/// The patch version.
		/// </value>
		public int Patch { get; private set; }

		/// <summary>
		/// Gets the pre-release version.
		/// </summary>
		/// <value>
		/// The pre-release version.
		/// </value>
		public string Prerelease { get; private set; }

		/// <summary>
		/// Gets the build version.
		/// </summary>
		/// <value>
		/// The build version.
		/// </value>
		public string Build { get; private set; }

		/// <summary>
		/// Returns a <see cref="string" /> that represents this instance.
		/// </summary>
		/// <returns>
		/// A <see cref="string" /> that represents this instance.
		/// </returns>
		public override string ToString()
		{
			var version = $"{Major}.{Minor}.{Patch}";
			if (!string.IsNullOrEmpty(Prerelease)) version += $"-{Prerelease}";
			if (!string.IsNullOrEmpty(Build)) version += $"+{Build}";
			return version;
		}

		/// <summary>
		/// Returns a <see cref="string" /> that represents this instance.
		/// </summary>
		/// <param name="parts">How many parts. Must be between 1 and 4, the fourth part being the PreRelease and/or Build.</param>
		/// <returns>
		/// A <see cref="string" /> that represents this instance.
		/// </returns>
		public string ToString(int parts)
		{
			if (parts < 1 || parts > 4) throw new Exception("Number of parts must be between 1 and 4");

			if (parts == 1) return $"{Major}";
			else if (parts == 2) return $"{Major}.{Minor}";
			else if (parts == 3) return $"{Major}.{Minor}.{Patch}";
			else return ToString();
		}

		/// <summary>
		/// Compares the current instance with another object of the same type and returns an integer that indicates
		/// whether the current instance precedes, follows, or occurs in the same position in the sort order as the
		/// other object.
		/// </summary>
		/// <param name="obj">An object to compare with this instance.</param>
		/// <returns>
		/// A value that indicates the relative order of the objects being compared.
		/// The return value has these meanings:
		/// - Less than zero: This instance precedes <paramref name="obj" /> in the sort order.
		/// - Zero: This instance occurs in the same position in the sort order as <paramref name="obj" />.
		///  - Greater than zero: This instance follows <paramref name="obj" /> in the sort order.
		/// </returns>
		public int CompareTo(object obj)
		{
			return CompareTo((SemVersion)obj);
		}

		/// <summary>
		/// Compares the current instance with another object of the same type and returns an integer that indicates
		/// whether the current instance precedes, follows, or occurs in the same position in the sort order as the
		/// other object.
		/// </summary>
		/// <param name="other">An object to compare with this instance.</param>
		/// <returns>
		/// A value that indicates the relative order of the objects being compared.
		/// The return value has these meanings:
		/// - Less than zero: This instance precedes <paramref name="other" /> in the sort order.
		/// - Zero: This instance occurs in the same position in the sort order as <paramref name="other" />.
		/// - Greater than zero: This instance follows <paramref name="other" /> in the sort order.
		/// </returns>
		public int CompareTo(SemVersion other)
		{
			if (other is null) return 1;

			var r = this.CompareByPrecedence(other);
			if (r != 0) return r;

			r = CompareComponent(this.Build, other.Build);
			return r;
		}

		/// <summary>
		/// Compares to semantic versions by precedence.
		/// This does the same as a Equals, but ignores the build information.
		/// </summary>
		/// <param name="other">The semantic version.</param>
		/// <returns><c>true</c> if the version precedence matches.</returns>
		public bool PrecedenceMatches(SemVersion other)
		{
			return CompareByPrecedence(other) == 0;
		}

		/// <summary>
		/// Compares to semantic versions by precedence.
		/// This does the same as a Equals, but ignores the build information.
		/// </summary>
		/// <param name="other">The semantic version.</param>
		/// <returns>
		/// A value that indicates the relative order of the objects being compared.
		/// The return value has these meanings:
		/// - Less than zero: This instance precedes <paramref name="other" /> in the version precedence.
		/// - Zero: This instance has the same precedence as <paramref name="other" />.
		/// - Greater than zero: This instance has creater precedence as <paramref name="other" />.
		/// </returns>
		public int CompareByPrecedence(SemVersion other)
		{
			if (other is null) return 1;

			var r = this.Major.CompareTo(other.Major);
			if (r != 0) return r;

			r = this.Minor.CompareTo(other.Minor);
			if (r != 0) return r;

			r = this.Patch.CompareTo(other.Patch);
			if (r != 0) return r;

			r = CompareComponent(this.Prerelease, other.Prerelease, true);
			return r;
		}

		/// <summary>
		/// Determines whether the specified <see cref="object" /> is equal to this instance.
		/// </summary>
		/// <param name="obj">The <see cref="object" /> to compare with this instance.</param>
		/// <returns>
		///   <c>true</c> if the specified <see cref="object" /> is equal to this instance; otherwise, <c>false</c>.
		/// </returns>
		public override bool Equals(object obj)
		{
			if (obj is null) return false;

			if (ReferenceEquals(this, obj)) return true;

			var other = (SemVersion)obj;

			return this.Major == other.Major &&
				this.Minor == other.Minor &&
				this.Patch == other.Patch &&
				string.Equals(this.Prerelease, other.Prerelease, StringComparison.Ordinal) &&
				string.Equals(this.Build, other.Build, StringComparison.Ordinal);
		}

		/// <summary>
		/// Returns a hash code for this instance.
		/// </summary>
		/// <returns>
		/// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.
		/// </returns>
		public override int GetHashCode()
		{
			unchecked
			{
				int result = this.Major.GetHashCode();
				result = (result * 31) + this.Minor.GetHashCode();
				result = (result * 31) + this.Patch.GetHashCode();
				result = (result * 31) + this.Prerelease.GetHashCode();
				result = (result * 31) + this.Build.GetHashCode();
				return result;
			}
		}

		/// <summary>
		/// For serialization.
		/// </summary>
		/// <param name="info">The serialization info.</param>
		/// <param name="context">The context.</param>
		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			if (info == null) throw new ArgumentNullException("info");
			info.AddValue("SemVersion", ToString());
		}

		/// <summary>
		/// Implicit conversion from string to SemVersion.
		/// </summary>
		/// <param name="version">The semantic version.</param>
		/// <returns>The SemVersion object.</returns>
		public static implicit operator SemVersion(string version)
		{
			return SemVersion.Parse(version);
		}

		/// <summary>
		/// The override of the equals operator.
		/// </summary>
		/// <param name="left">The left value.</param>
		/// <param name="right">The right value.</param>
		/// <returns>If left is equal to right <c>true</c>, else <c>false</c>.</returns>
		public static bool operator ==(SemVersion left, SemVersion right)
		{
			return SemVersion.Equals(left, right);
		}

		/// <summary>
		/// The override of the un-equal operator.
		/// </summary>
		/// <param name="left">The left value.</param>
		/// <param name="right">The right value.</param>
		/// <returns>If left is not equal to right <c>true</c>, else <c>false</c>.</returns>
		public static bool operator !=(SemVersion left, SemVersion right)
		{
			return !SemVersion.Equals(left, right);
		}

		/// <summary>
		/// The override of the greater operator.
		/// </summary>
		/// <param name="left">The left value.</param>
		/// <param name="right">The right value.</param>
		/// <returns>If left is greater than right <c>true</c>, else <c>false</c>.</returns>
		public static bool operator >(SemVersion left, SemVersion right)
		{
			return SemVersion.Compare(left, right) > 0;
		}

		/// <summary>
		/// The override of the greater than or equal operator.
		/// </summary>
		/// <param name="left">The left value.</param>
		/// <param name="right">The right value.</param>
		/// <returns>If left is greater than or equal to right <c>true</c>, else <c>false</c>.</returns>
		public static bool operator >=(SemVersion left, SemVersion right)
		{
			return left == right || left > right;
		}

		/// <summary>
		/// The override of the less operator.
		/// </summary>
		/// <param name="left">The left value.</param>
		/// <param name="right">The right value.</param>
		/// <returns>If left is less than right <c>true</c>, else <c>false</c>.</returns>
		public static bool operator <(SemVersion left, SemVersion right)
		{
			return SemVersion.Compare(left, right) < 0;
		}

		/// <summary>
		/// The override of the less than or equal operator.
		/// </summary>
		/// <param name="left">The left value.</param>
		/// <param name="right">The right value.</param>
		/// <returns>If left is less than or equal to right <c>true</c>, else <c>false</c>.</returns>
		public static bool operator <=(SemVersion left, SemVersion right)
		{
			return left == right || left < right;
		}

		private static int CompareComponent(string a, string b, bool lower = false)
		{
			var aEmpty = string.IsNullOrEmpty(a);
			var bEmpty = string.IsNullOrEmpty(b);
			if (aEmpty && bEmpty)
				return 0;

			if (aEmpty)
				return lower ? 1 : -1;
			if (bEmpty)
				return lower ? -1 : 1;

			var aComps = a.Split('.');
			var bComps = b.Split('.');

			var minLen = Math.Min(aComps.Length, bComps.Length);
			for (int i = 0; i < minLen; i++)
			{
				var ac = aComps[i];
				var bc = bComps[i];
				var isanum = int.TryParse(ac, out int anum);
				var isbnum = int.TryParse(bc, out int bnum);
				int r;
				if (isanum && isbnum)
				{
					r = anum.CompareTo(bnum);
					if (r != 0) return anum.CompareTo(bnum);
				}
				else
				{
					if (isanum)
						return -1;
					if (isbnum)
						return 1;
					r = string.CompareOrdinal(ac, bc);
					if (r != 0)
						return r;
				}
			}

			return aComps.Length.CompareTo(bComps.Length);
		}
	}
}
