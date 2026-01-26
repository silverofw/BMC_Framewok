using System;
using System.Collections;
using System.Collections.Generic;
namespace Core
{
    public static class Rand
    {
        public static long Next(this Random random, long min, long max)
        {
            if (min > max)
            {
                throw new ArgumentOutOfRangeException("min", "min must be less than or equal to max");
            }

            // Calculate the range
            ulong range = (ulong)(max - min);

            // Generate a random 64-bit integer within the range
            ulong randomValue = (ulong)random.NextInt64();

            // Return the random value within the specified range
            return (long)(randomValue % range) + min;
        }
        public static long NextInt64(this Random random)
        {
            byte[] buffer = new byte[8];
            random.NextBytes(buffer);
            return BitConverter.ToInt64(buffer, 0);
        }
    }
}