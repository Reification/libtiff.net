using System;

namespace R9N {
	public struct VectorD2 {
		public double x;
		public double y;

		public double width { get { return x; } set { x = value; } }
		public double height { get { return y; } set { y = value; } }
		public double area {  get { return x * y; } }

		public VectorD2 Abs() {
			return new VectorD2 { x = Math.Abs(x), y = Math.Abs(y) };
		}

		public VectorD2 Ceiling() {
			return new VectorD2 { x = Math.Ceiling(x), y = Math.Ceiling(y) };
		}

		public VectorD2 Floor() {
			return new VectorD2 { x = Math.Floor(x), y = Math.Floor(y) };
		}

		public VectorD2 Round() {
			return new VectorD2 { x = Math.Round(x), y = Math.Round(y) };
		}

		public VectorD2 Truncate() {
			return new VectorD2 { x = Math.Truncate(x), y = Math.Truncate(y) };
		}


		public VectorD2 xy {
			get { return this; }
		}

		public VectorD2 yx {
			get { return new VectorD2 { x = y, y = x }; }
		}

		public double Min() {
			return Math.Min(x, y);
		}

		public double Max() {
			return Math.Max(x, y);
		}

		public override string ToString() {
			return string.Format("({0}, {1})", x, y);
		}

		public static implicit operator VectorD2(double v) {
			return new VectorD2 { x = v, y = v };
		}

		public static implicit operator VectorD2(VectorD3 v3) {
			return new VectorD2 { x = v3.x, y = v3.y };
		}

		public static bool operator ==(VectorD2 a, VectorD2 b) {
			return a.x == b.x && a.y == b.y;
		}

		public static bool operator !=(VectorD2 a, VectorD2 b) {
			return a.x != b.x || a.y != b.y;
		}

		public override bool Equals(object obj) {
			return base.Equals(obj);
		}

		public override int GetHashCode() {
			return base.GetHashCode();
		}

		public static VectorD2 operator -(VectorD2 v) {
			return new VectorD2 { x = -v.x, y = -v.y };
		}

		public static VectorD2 operator *(VectorD2 v, double s) {
			return new VectorD2 { x = v.x * s, y = v.y * s };
		}

		public static VectorD2 operator *(double s, VectorD2 v) {
			return new VectorD2 { x = v.x * s, y = v.y * s };
		}

		public static VectorD2 operator /(VectorD2 v, double s) {
			return new VectorD2 { x = v.x / s, y = v.y / s };
		}

		public static VectorD2 operator +(VectorD2 v, VectorD2 v2) {
			return new VectorD2 { x = v.x + v2.x, y = v.y + v2.y };
		}

		public static VectorD2 operator -(VectorD2 v, VectorD2 v2) {
			return new VectorD2 { x = v.x - v2.x, y = v.y - v2.y };
		}

		public static VectorD2 operator *(VectorD2 v, VectorD2 v2) {
			return new VectorD2 { x = v.x * v2.x, y = v.y * v2.y };
		}

		public static VectorD2 operator /(VectorD2 v, VectorD2 v2) {
			return new VectorD2 { x = v.x / v2.x, y = v.y / v2.y };
		}

		public static readonly VectorD2 v00 = 0;
		public static readonly VectorD2 v10 = new VectorD2 { x = 1, y = 0 };
		public static readonly VectorD2 v01 = new VectorD2 { x = 0, y = 1 };
		public static readonly VectorD2 v11 = 1;
	}

	public struct VectorD3 {
		public double x;
		public double y;
		public double z;

		public double width { get { return x; } }
		public double height { get { return y; } }
		public double depth { get { return z; } }

		public VectorD3 xyz {
			get { return this; }
		}

		public VectorD3 yxz {
			get { return new VectorD3 { x = y, y = x, z = z }; }
		}

		public VectorD3 Abs() {
			return new VectorD3 { x = Math.Abs(x), y = Math.Abs(y), z = Math.Abs(z) };
		}

		public VectorD3 Ceiling() {
			return new VectorD3 { x = Math.Ceiling(x), y = Math.Ceiling(y), z = Math.Ceiling(z) };
		}

		public VectorD3 Floor() {
			return new VectorD3 { x = Math.Floor(x), y = Math.Floor(y), z = Math.Floor(z) };
		}

		public VectorD3 Round() {
			return new VectorD3 { x = Math.Round(x), y = Math.Round(y), z = Math.Round(z) };
		}

		public VectorD3 Truncate() {
			return new VectorD3 { x = Math.Truncate(x), y = Math.Truncate(y), z = Math.Truncate(z) };
		}

		public double Min() {
			return Math.Min(x, Math.Min(y, z));
		}

		public double Max() {
			return Math.Max(x, Math.Max(y, z));
		}

		public override string ToString() {
			return string.Format("({0}, {1}, {2})", x, y, z);
		}

		public static implicit operator VectorD3(double v) {
			return new VectorD3 { x = v, y = v, z = v };
		}

		public static implicit operator VectorD3(VectorD2 v2) {
			return new VectorD3 { x = v2.x, y = v2.y, z = 0 };
		}

		public static bool operator ==(VectorD3 a, VectorD3 b) {
			return a.x == b.x && a.y == b.y && a.z == b.z;
		}

		public static bool operator !=(VectorD3 a, VectorD3 b) {
			return a.x != b.x || a.y != b.y || a.z != b.z;
		}

		public override bool Equals(object obj) {
			return base.Equals(obj);
		}

		public override int GetHashCode() {
			return base.GetHashCode();
		}

		public static VectorD3 operator -(VectorD3 v) {
			return new VectorD3 { x = -v.x, y = -v.y, z = -v.z };
		}

		public static VectorD3 operator *(VectorD3 v, double s) {
			return new VectorD3 { x = v.x * s, y = v.y * s, z = v.z * s };
		}

		public static VectorD3 operator *(double s, VectorD3 v) {
			return new VectorD3 { x = v.x * s, y = v.y * s, z = v.z * s };
		}

		public static VectorD3 operator /(VectorD3 v, double s) {
			return new VectorD3 { x = v.x / s, y = v.y / s, z = v.z / s };
		}

		public static VectorD3 operator +(VectorD3 v, VectorD3 v2) {
			return new VectorD3 { x = v.x + v2.x, y = v.y + v2.y, z = v.z + v2.z };
		}

		public static VectorD3 operator -(VectorD3 v, VectorD3 v2) {
			return new VectorD3 { x = v.x - v2.x, y = v.y - v2.y, z = v.z - v2.z };
		}

		public static VectorD3 operator *(VectorD3 v, VectorD3 v2) {
			return new VectorD3 { x = v.x * v2.x, y = v.y * v2.y, z = v.z * v2.z };
		}

		public static VectorD3 operator /(VectorD3 v, VectorD3 v2) {
			return new VectorD3 { x = v.x / v2.x, y = v.y / v2.y, z = (v.z == 0 && v2.z == 0) ? 0 : v.z / v2.z };
		}

		public static VectorD3 operator *(VectorD3 v, VectorD2 v2) {
			return new VectorD3 { x = v.x * v2.x, y = v.y * v2.y, z = v.z };
		}

		public static VectorD3 operator /(VectorD3 v, VectorD2 v2) {
			return new VectorD3 { x = v.x / v2.x, y = v.y / v2.y, z = v.z };
		}

		public static readonly VectorD3 v000 = 0;
		public static readonly VectorD3 v100 = new VectorD3 { x = 1, y = 0, z = 0 };
		public static readonly VectorD3 v010 = new VectorD3 { x = 0, y = 1, z = 0 };
		public static readonly VectorD3 v001 = new VectorD3 { x = 0, y = 0, z = 1 };
		public static readonly VectorD3 v110 = new VectorD3 { x = 1, y = 1, z = 0 };
		public static readonly VectorD3 v101 = new VectorD3 { x = 1, y = 0, z = 1 };
		public static readonly VectorD3 v011 = new VectorD3 { x = 0, y = 1, z = 1 };
		public static readonly VectorD3 v111 = 1;
	}
}
