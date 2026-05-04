using System;

namespace InfiniteMap
{
    /// <summary>
    /// 三維網格座標 (不超過五個字)
    /// </summary>
    public struct Pos3 : IEquatable<Pos3>
    {
        public int x;
        public int y;
        public int h;

        public Pos3(int x, int y, int h = 0)
        {
            this.x = x;
            this.y = y;
            this.h = h;
        }

        public static Pos3 Zero => new Pos3(0, 0, 0);

        /// <summary>
        /// 計算此座標所屬的 Chunk 座標
        /// </summary>
        public CPos ToCPos(int chunkSize = 16)
        {
            int cx = x >= 0 ? x / chunkSize : (x + 1) / chunkSize - 1;
            int cy = y >= 0 ? y / chunkSize : (y + 1) / chunkSize - 1;
            return new CPos(cx, cy);
        }

        public bool Equals(Pos3 other) => x == other.x && y == other.y && h == other.h;
        public override bool Equals(object obj) => obj is Pos3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y, h);
        public override string ToString() => $"({x}, {y}, {h})";

        public static bool operator ==(Pos3 a, Pos3 b) => a.Equals(b);
        public static bool operator !=(Pos3 a, Pos3 b) => !a.Equals(b);
    }

    /// <summary>
    /// 區塊座標 (Chunk Position)
    /// </summary>
    public struct CPos : IEquatable<CPos>
    {
        public int x;
        public int y;

        public CPos(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public bool Equals(CPos other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is CPos other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y);
        public override string ToString() => $"[C:{x}, {y}]";

        public static bool operator ==(CPos a, CPos b) => a.Equals(b);
        public static bool operator !=(CPos a, CPos b) => !a.Equals(b);
    }
}