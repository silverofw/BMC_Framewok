using System;

namespace BMC.Core
{
    [System.Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public static Vec2 Zero      = new Vec2();
        public static Vec2 One       = new Vec2( 1,  1);

        public static Vec2 Up        = new Vec2( 0,  1);
        public static Vec2 Up2       = new Vec2( 0,  2);
        public static Vec2 Down      = new Vec2( 0, -1);
        public static Vec2 Down2     = new Vec2( 0, -2);
        public static Vec2 Left      = new Vec2(-1,  0);
        public static Vec2 Left2     = new Vec2(-2,  0);
        public static Vec2 Right     = new Vec2( 1,  0);
        public static Vec2 Right2    = new Vec2( 2,  0);

        public static Vec2 UpLeft    = new Vec2(-1,  1);
        public static Vec2 UpRight   = new Vec2( 1,  1);
        public static Vec2 DownLeft  = new Vec2(-1, -1);
        public static Vec2 DownRight = new Vec2( 1, -1);

        public static Vec2 Height2 = new Vec2(0, 0, 2);
        public static Vec2 Height1 = new Vec2(0, 0, 1);
        public static Vec2 Deep1 = new Vec2(0, 0, -1);
        public static Vec2 Deep2 = new Vec2(0, 0, -2);

        public static Vec2 Max = new Vec2(int.MaxValue, int.MaxValue);

        public int x; 
        public int y;
        public int h;

        public const int mapWidth = 15;
        public const int bigMapWidth = 1000;

        public Vec2(int x = 0, int y = 0, int h = 0) 
        {
            this.x = x; 
            this.y = y; 
            this.h = h;
        }

        public static Vec2 operator +(Vec2 left, Vec2 right)
        {
            return new Vec2(left.x + right.x, left.y + right.y, left.h + right.h);
        }
        public static Vec2 operator -(Vec2 left, Vec2 right)
        {
            return new Vec2(left.x - right.x, left.y - right.y, left.h - right.h);
        }
        public static Vec2 operator *(Vec2 left, int right)
        {
            return new Vec2(left.x * right, left.y * right, left.h);
        }

        /// <summary>
        /// 最後三個位數留給map
        /// </summary>
        /// <param name="mapId"></param>
        /// <returns></returns>
        public int ToMap(int mapId) {
            Regular(ref mapId, ref this);
            return mapId * bigMapWidth + x % mapWidth + (y % mapWidth) * mapWidth;
        }

        /// <summary>
        /// 轉換為地圖座標
        /// </summary>
        /// <param name="mapId"></param>
        /// <returns></returns>
        public Vec2 ToMapPos(int mapId)
        {
            return new Vec2(x + (mapId / bigMapWidth * mapWidth), y + (mapId % bigMapWidth * mapWidth), h);
        }

        /// <summary>
        /// 轉換為本地座標
        /// </summary>
        /// <param name="mapPos"></param>
        /// <returns></returns>
        public Vec2 ToLocalPos(int mapId)
        {
            return new Vec2(x - (mapId / bigMapWidth * mapWidth), y - (mapId % bigMapWidth * mapWidth), h);
        }

        public static void Regular(ref int mapId, ref Vec2 pos)
        {
            if (pos.x >= mapWidth)
            {
                mapId += pos.x / mapWidth * bigMapWidth;
                pos.x -= pos.x / mapWidth * mapWidth;
            }
            if (pos.y >= mapWidth)
            {
                mapId += pos.y / mapWidth;
                pos.y -= pos.y / mapWidth * mapWidth;
            }
            if (pos.x < 0)
            {
                mapId += (pos.x / mapWidth - 1) * bigMapWidth;
                pos.x -= (pos.x / mapWidth - 1) * mapWidth;
            }
            if (pos.y < 0)
            {
                mapId += (pos.y / mapWidth - 1);
                pos.y -= (pos.y / mapWidth - 1) * mapWidth;
            }
        }

        public static int Dis(Vec2 pos1, Vec2 pos2)
        {
            return Math.Abs(pos1.x - pos2.x) + Math.Abs(pos1.y - pos2.y);
        }

        /// <summary>
        /// 计算两个向量之间的夹角（弧度）
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public double AngleTo(Vec2 other)
        {
            double dotProduct = this.x * other.x + this.y * other.y;
            double magnitudeA = Math.Sqrt(this.x * this.x + this.y * this.y);
            double magnitudeB = Math.Sqrt(other.x * other.x + other.y * other.y);

            return Math.Acos(dotProduct / (magnitudeA * magnitudeB));
        }

        public static Vec2 toMapPos(int bigMapIndex)
        {
            var mapId = bigMapIndex / bigMapWidth;
            return new Vec2(
                bigMapIndex % bigMapWidth % mapWidth + (mapId / bigMapWidth * mapWidth), 
                bigMapIndex % bigMapWidth / mapWidth + (mapId % bigMapWidth * mapWidth)
                );
        }

        public static Vec2 fromStr(string str)
        {
            var s = str.Split(',');
            return new Vec2(int.Parse(s[0]), int.Parse(s[1]), int.Parse(s[2]));
        }


        public override bool Equals(object obj)
        {
            if (obj is Vec2 other)
            {
                return this.x == other.x && this.y == other.y && this.h == other.h;
            }
            return false;
        }

        public bool Equals(Vec2 other)
        {
            return this.x == other.x && this.y == other.y && this.h == other.h;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + x;
                hash = hash * 31 + y;
                hash = hash * 31 + h;
                return hash;
            }
        }

        public static bool operator ==(Vec2 lhs, Vec2 rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(Vec2 lhs, Vec2 rhs)
        {
            return !lhs.Equals(rhs);
        }

        public override string ToString()
        {
            return $"{x},{y},{h}";
        }
    }
}
