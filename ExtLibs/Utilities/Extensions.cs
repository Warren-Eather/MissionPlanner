﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GMap.NET;
using MissionPlanner.Comms;
using Newtonsoft.Json;

namespace MissionPlanner.Utilities
{

    [StructLayout(LayoutKind.Explicit, Size = 8, Pack = 1)]
    public struct typeunion
    {
        [FieldOffset(0)] public bool boolean;

        ///< sizeof(bool) is implementation-defined, so it has to be handled separately
        [FieldOffset(0)] public Byte u8;

        ///< Also char
        [FieldOffset(0)] public SByte s8;

        [FieldOffset(0)] public UInt16 u16;
        [FieldOffset(0)] public Int16 s16;
        [FieldOffset(0)] public UInt32 u32;
        [FieldOffset(0)] public Int32 s32;

        ///< Also float, possibly double, possibly long double (depends on implementation)
        [FieldOffset(0)] public UInt64 u64;

        [FieldOffset(0)] public Int64 s64;

        [FieldOffset(0)] public float f32;
        [FieldOffset(0)] public double d64;

        public Byte this[int index]
        {
            get { return BitConverter.GetBytes(u64)[index]; }
            set
            {
                var temp = BitConverter.GetBytes(u64);
                temp[index] = value;
                u64 = BitConverter.ToUInt64(temp, 0);
            }
        }

        ///< Also double, possibly float, possibly long double (depends on implementation)
        public IReadOnlyList<Byte> bytes
        {
            get { return BitConverter.GetBytes(u64); }
            /* set
            {
                var temp = value.ToArray();
                Array.Resize(ref temp, 8);
                u64 = BitConverter.ToUInt64(temp, 0);
            }*/
        }

        public typeunion(bool b1 = false)
        {
            boolean = false;
            u8 = 0;
            s8 = 0;
            u16 = 0;
            u32 = 0;
            u64 = 0;
            s8 = 0;
            s16 = 0;
            s32 = 0;
            s64 = 0;
            f32 = 0;
            d64 = 0;
        }
    }

    public class EqualityComparer<T> : IEqualityComparer<T>
    {
        public EqualityComparer(Func<T, T, bool> cmp)
        {
            this.cmp = cmp;
        }
        public bool Equals(T x, T y)
        {
            return cmp(x, y);
        }

        public int GetHashCode(T obj)
        {
            return 0;
        }

        public Func<T, T, bool> cmp { get; }
    }

    public static class Extensions
    {
        public static Action MessageLoop;
        //https://medium.com/rubrikkgroup/understanding-async-avoiding-deadlocks-e41f8f2c6f5d
        public static T AwaitSync<T>(this Task<T> infunc)
        {
            var tsk = Task.Run<T>(async () =>
            {
                return await infunc.ConfigureAwait(false);
            });

            return tsk.GetAwaiter().GetResult();
        }

        public static IEnumerable<IEnumerable<T>> Chunk<T>(this IEnumerable<T> source, int chunksize)
        {
            while (source.Any())
            {
                yield return source.Take(chunksize);
                source = source.Skip(chunksize);
            }
        }

        /// <summary>
        /// Chunk based on a field selector from the type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <param name="checkmatching"></param>
        /// <returns></returns>
        public static IEnumerable<IEnumerable<T>> ChunkByField<T>(this IEnumerable<T> source, Func<T,T, int, bool> checkmatching)
        {
            while (source.Any())
            {
                // store the first element to compare against
                T first = source.First();
                int include = 0;
                // build the list by comparing the first to all after it
                var sublist = source.TakeWhile((a,count) =>
                {
                    if (first.Equals(a))
                    {
                        include++;
                        return true;
                    }

                    var check = checkmatching(first, a, count);
                    if(check)
                        include++;
                    return check;
                }).ToList(); // tolist improves performance vs ienumerable requery on every access
                yield return sublist;
                // continue on in the list
                source = source.Skip(include);
            }
        }

        public static IEnumerable<T> Traverse<T>(this IEnumerable<T> items, 
            Func<T, IEnumerable<T>> childSelector)
        {
            var stack = new Stack<T>(items);
            while(stack.Any())
            {
                var next = stack.Pop();
                yield return next;
                foreach(var child in childSelector(next))
                    stack.Push(child);
            }
        }

        public static void AddRange<T>(this IList<T> list, IEnumerable<T> extras )
        {
            extras.ForEach(a => list.Add(a));
        }

        public static void Stop(this System.Threading.Timer timer)
        {
            timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public static void Start(this System.Threading.Timer timer, int intervalms)
        {
            timer.Change(intervalms, intervalms);
        }

        public static byte[] ToByteArray(this char[] input)
        {
            return input.Select(a => (byte) a).ToArray();
        }

        /// <summary>
        /// from null terminated c-string
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static string FromCString(this byte[] input)
        {
            string st = Encoding.ASCII.GetString(input);

            int pos = st.IndexOf('\0');

            if (pos != -1)
            {
                st = st.Substring(0, pos);
            }

            return st;
        }

        /// <summary>
        /// get upto 64 bits at a time
        /// </summary>
        /// <param name="buff"></param>
        /// <param name="pos"></param>
        /// <param name="len"></param>
        /// <returns></returns>
        public static ulong getbitu(this byte[] buff, uint pos, uint len)
        {
            ulong bits = 0;
            uint i;
            for (i = pos; i < pos + len; i++)
                bits = (ulong)((bits << 1) + (byte)((buff[i / 8] >> (int)(7 - i % 8)) & 1u));
            return bits;
        }

        public static long getbits(this byte[] buff, uint pos, uint len)
        {
            var bits = getbitu(buff, pos, len);
            if (len <= 0 || 64 <= len || !((bits & (1u << (int)(len - 1))) != 0))
                return (long)bits;
            return (long)(bits | (~0ul << (int)len));
        }

        public static T GetBitOffsetLength<T>(this byte[] input, int start, int offset, int length, bool signed, double resolution = 0)
        {
            if (resolution == 0)
                resolution = 1;

            if (typeof(T) == typeof(string))
            {
                return (T)(object)Encoding.ASCII.GetString(BitConverter.GetBytes(input.getbitu((uint)offset, (uint)length)));
            }

            if (typeof(T) == typeof(int) && signed)
            {
                return (T)(object)input.getbits((uint)offset, (uint)length);
            }
            if (typeof(T) == typeof(uint) && !signed)
            {
                return (T)(object)input.getbitu((uint)offset, (uint)length);
            }

            if (typeof(T) == typeof(float) && signed)
            {
                return (T)(object)new typeunion() { u64 = input.getbitu((uint)offset, (uint)length) }.f32;
            }
            if (typeof(T) == typeof(double) && signed)
            {
                return (T)(object)new typeunion() { u64 = input.getbitu((uint)offset, (uint)length) }.d64;
            }
            if (typeof(T) == typeof(long) && signed)
            {
                return (T)(object)new typeunion() { u64 = input.getbitu((uint)offset, (uint)length) }.s64;
            }
            if (typeof(T) == typeof(DateTime))
            {
                return (T)(object)new typeunion() { u64 = input.getbitu((uint)offset, (uint)length) }.u32;
            }

            return default(T);
        }

        public static string ToHexString(this byte[] input)
        {
            StringBuilder hex = new StringBuilder(input.Length * 2);
            foreach (byte b in input)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }

        public static string ToHexString(this IEnumerable<byte> input)
        {
            StringBuilder hex = new StringBuilder(input.Count() * 2);
            foreach (byte b in input)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }

        public static string ToJSON(this object msg, Formatting fmt)
        {
            return JsonConvert.SerializeObject(msg, fmt, new JsonSerializerSettings()
            {
                Error =
                    (sender, args) => { args.ErrorContext.Handled = true; }
            });
        }

        public static string ToJSONWithType(this object msg)
        {
            return String.Format("\"{0}\": ",msg.GetType().Name) + msg.ToJSON(Formatting.Indented);
        }

        public static string ToJSON(this object msg)
        {
            return msg.ToJSON(Formatting.Indented);
        }

        public static string ToJSON(this System.Type a_Type)
        {
            var TypeBlob = a_Type.GetFields().ToDictionary(x => x.Name, x => x.GetValue(null));
            a_Type.GetProperties().ToDictionary(x => x.Name, x =>
            {
                try
                {
                    return x.GetValue(null);
                }
                catch (Exception ex)
                {
                    return ex.ToString();
                }
            }).ForEach(x => TypeBlob.Add(x.Key, x.Value));
            return JsonConvert.SerializeObject(TypeBlob);
        }

        public static T FromJSON<T>(this string msg)
        {
            return JsonConvert.DeserializeObject<T>(msg);
        }

        public static string CleanString(this string dirtyString)
        {
            return new String(dirtyString.Where(Char.IsLetterOrDigit).ToArray());
        }

        public static string RemoveFromEnd(this string s, string suffix)
        {
            if (s.EndsWith(suffix))
            {
                return s.Substring(0, s.Length - suffix.Length);
            }
            else
            {
                return s;
            }
        }
        
        public static byte[] MakeSize(this byte[] buffer, int length)
        {
            if (buffer.Length == length)
                return buffer;
            Array.Resize(ref buffer, length);
            return buffer;
        }

        public static byte[] MakeBytesSize(this string item, int length)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            var buffer = ASCIIEncoding.ASCII.GetBytes(item);
            if (buffer.Length == length)
                return buffer;
            Array.Resize(ref buffer, length);
            return buffer;
        }
        public static byte[] MakeBytes(this string item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            var buffer = ASCIIEncoding.ASCII.GetBytes(item);
            return buffer;
        }

        public static char[] MakeCharSize(this string item, int length)
        {
            var buffer = item.ToCharArray();
            if (buffer.Length == length)
                return buffer;
            Array.Resize(ref buffer, length);
            return buffer;
        }
        public static MemoryStream ToMemoryStream(this byte[] buffer)
        {
            return new MemoryStream(buffer);
        }

        public static string TrimUnPrintable(this string input)
        {
            return Regex.Replace(input, @"[^\u0020-\u007E]", String.Empty);
        }

        public static double ConvertToDouble(this object input)
        {
            if (input.GetType() == typeof(float))
            {
                return (float)input;
            }
            if (input.GetType() == typeof(double))
            {
                return (double)input;
            }
            if (input.GetType() == typeof(ulong))
            {
                return (ulong)input;
            }
            if (input.GetType() == typeof(long))
            {
                return (long)input;
            }
            if (input.GetType() == typeof(int))
            {
                return (int)input;
            }
            if (input.GetType() == typeof(uint))
            {
                return (uint)input;
            }
            if (input.GetType() == typeof(short))
            {
                return (short)input;
            }
            if (input.GetType() == typeof(ushort))
            {
                return (ushort)input;
            }
            if (input.GetType() == typeof(byte))
            {
                return (byte)input;
            }
            if (input.GetType() == typeof(sbyte))
            {
                return (sbyte)input;
            }
            if (input.GetType() == typeof(bool))
            {
                return (bool)input ? 1 : 0;
            }
            if (input.GetType() == typeof(string))
            {
                double ans = 0;
                if (double.TryParse((string)input, out ans))
                {
                    return ans;
                }
            }
            if (input is Enum)
            {
                return Convert.ToInt32(input);
            }

            if (input == null)
                throw new Exception("Bad Type Null");
            else
                throw new Exception("Bad Type " + input.GetType().ToString());
        }

        public static void CallWithTimeout(this Action action, int timeoutMilliseconds)
        {
            Thread threadToKill = null;
            Action wrappedAction = () =>
            {
                threadToKill = Thread.CurrentThread;
                action();
            };

            var result = wrappedAction.BeginInvoke(null, null);
            if (result.AsyncWaitHandle.WaitOne(timeoutMilliseconds))
            {
                wrappedAction.EndInvoke(result);
            }
            else
            {
                threadToKill.Abort();
                throw new TimeoutException();
            }
        }

        public static void CallWithTimeout<T>(Action<T> action, int timeoutMilliseconds, T data)
        {
            Thread threadToKill = null;
            Action wrappedAction = () =>
            {
                threadToKill = Thread.CurrentThread;
                action(data);
            };

            var result = wrappedAction.BeginInvoke(null, null);
            if (result.AsyncWaitHandle.WaitOne(timeoutMilliseconds))
            {
                wrappedAction.EndInvoke(result);
            }
            else
            {
                threadToKill.Abort();
                throw new TimeoutException();
            }
        }

        public static void Add<T, T2>(this List<Tuple<T, T2>> input, T in1, T2 in2)
        {
            input.Add(new Tuple<T, T2>(in1, in2));
        }

        public static void Add<T, T2, T3>(this List<Tuple<T, T2, T3>> input, T in1, T2 in2, T3 in3)
        {
            input.Add(new Tuple<T, T2, T3>(in1, in2, in3));
        }

        public static bool IsNumber(this string value)
        {
            decimal num;
            return decimal.TryParse(value, out num);
        }

        public static bool IsNumber(this object value)
        {
            return IsNumber(value?.GetType());
        }

        public static bool IsNumber(this Type value)
        {
            if (value == null)
            {
                return false;
            }

            switch (Type.GetTypeCode(value))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return true;
                case TypeCode.Object:
                    if (value.IsGenericType && value.GetGenericTypeDefinition() == typeof(Nullable<>))
                    {
                        return Nullable.GetUnderlyingType(value).IsNumber();
                    }

                    return false;
                default:
                    return false;
            }
        }

        public static IEnumerable<T> IterateTreeType<T>(this T root, Func<T, IEnumerable<T>> childrenF)
        {
            var q = new List<T>() { root };
            while (q.Any())
            {
                var c = q[0];
                q.RemoveAt(0);
                q.AddRange(childrenF(c) ?? Enumerable.Empty<T>());
                yield return c;
            }
        }

        public static IEnumerable<T> IterateTree<T>(this IEnumerable<T> root, Func<T, IEnumerable<T>> childrenF)
        {
            var q = new List<T>();
            q.AddRange(root);
            while (q.Any())
            {
                var c = q[0];
                q.RemoveAt(0);
                q.AddRange(childrenF(c) ?? Enumerable.Empty<T>());
                yield return c;
            }
        }

        public static IEnumerable<T> Flatten<T>(this IEnumerable collection)
        {
            foreach (var o in collection)
            {
                if (o is IEnumerable)
                {
                    foreach (T t in Flatten<T>((IEnumerable)o))
                      yield return t;
                }
                else
                    yield return (T)o;
            }
        }

        public static IEnumerable<MAVLink.MAVLinkMessage> GetMessageOfType(this CommsFile commsFile,
            MAVLink.MAVLINK_MSG_ID[] packetids = null, bool hasTimestamp = false)
        {
            var parse = new MAVLink.MavlinkParse(hasTimestamp);

            var list = packetids.Cast<uint>();

            while (commsFile.BytesToRead > 0)
            {
                var packet = parse.ReadPacket(commsFile.BaseStream);
                if (packet == null)
                    continue;
                if (packetids == null || list.Contains(packet.msgid))
                    yield return packet;
            }
        }

        public static void DeDupOrderedList<T>(this List<T> list)
        {
            int a = 0;
            while (a < (list.Count-2))
            {
                if (list[a].Equals(list[a + 1]))
                {
                    list.RemoveAt(a + 1);
                    continue;
                }

                a++;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <typeparam name="TAccumulate"></typeparam>
        /// <param name="source">Source list</param>
        /// <param name="seed">Start value</param>
        /// <param name="func"></param>
        /// <returns></returns>
        public static TAccumulate Aggregate<TSource, TAccumulate>(this IEnumerable<TSource> source, TAccumulate seed,
            Func<TAccumulate, TSource, TSource, TAccumulate> func)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (func == null)
            {
                throw new ArgumentNullException("func");
            }
            if (source.Count() == 0)
                return seed;
            TAccumulate val = seed;
            TSource last = source.First();
            int a = -1;
            foreach (TSource item in source)
            {
                a++;
                if (a == 0)
                {
                    last = item;
                    continue;
                }

                val = func(val, last, item);
                last = item;
            }
            return val;
        }

        public static IEnumerable<int> SteppedRange(int fromInclusive, int toExclusive, int step)
        {
            for (var i = fromInclusive; i < toExclusive; i += step)
            {
                yield return i;
            }
        }

        public static IEnumerable<double> SteppedRange(double fromInclusive, double toExclusive, double step)
        {
            for (var i = fromInclusive; i < toExclusive; i += step)
            {
                yield return i;
            }
        }

        public static IEnumerable<T> CloseLoop<T>(this IEnumerable<T> list)
        {
            foreach (var item in list)
            {
                yield return item;
            }

            if (!list.First().Equals(list.Last()))
                yield return list.First();
        }

        public static IEnumerable<Tuple<T, T, T>> PrevNowNext<T>(this IEnumerable<T> list, T InitialValue = default(T), T InvalidValue = default(T))
        {
            T prev = InvalidValue;
            T now = InvalidValue;
            T next = InitialValue;
            int a = -1;
            foreach (var item in list)
            {
                a++;
                prev = now;
                now = next;
                next = item;
                if(a==0)
                    continue;
                yield return new Tuple<T, T, T>(prev, now, next);
            }

            yield return new Tuple<T, T, T>(now, next, InvalidValue);
        }

        public static uint SwapBytes(this uint x)
        {
            // swap adjacent 16-bit blocks
            x = (x >> 16) | (x << 16);
            // swap adjacent 8-bit blocks
            return ((x & 0xFF00FF00) >> 8) | ((x & 0x00FF00FF) << 8);
        }

        public static ushort SwapBytes(this ushort x)
        {
            // swap adjacent 8-bit blocks
            return (ushort)(((x & 0xFF00) >> 8) | ((x & 0x00FF) << 8));
        }

        public static byte[] HexStringToByteArray(this string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        public static string HexStringToSpacedHex(this string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => hex.Substring(x, 2)).Aggregate((a, b) => a + " " + b);
        }

        public static IEnumerable<Tuple<T, T>> NowNextBy2<T>(this IEnumerable<T> list)
        {
            T now = default(T);
            T next = default(T);

            int a = -1;
            foreach (var item in list)
            {
                a++;
                now = next;
                next = item;
                if(a % 2 == 0)
                    continue;
                yield return new Tuple<T, T>(now, next);
            }
        }

        public static object GetPropertyOrField(this object obj, string name)
        {
            var type = obj.GetType();
            var pi = type.GetProperty(name);
            if (pi == null)
            {
                var fi1 = type.GetField(name);
                return fi1.GetValue(obj);
            }
            return pi.GetValue(obj);
        }

        public static object GetPropertyOrFieldPrivate(this object obj, string name)
        {
            var type = obj.GetType();
            var pi = type.GetProperty(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (pi == null)
            {
                var fi1 = type.GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return fi1.GetValue(obj);
            }
            return pi.GetValue(obj);
        }

        public static int Search(this byte[] src, byte[] pattern, int startfrom = 0)
        {
            int maxFirstCharSlot = src.Length - pattern.Length + 1;
            int j;
            for (int i = startfrom; i < maxFirstCharSlot; i++)
            {
                if (src[i] != pattern[0]) continue;//comp only first byte
        
                // found a match on first byte, it tries to match rest of the pattern
                for (j = pattern.Length - 1; j >= 1 && src[i + j] == pattern[j]; j--) ;
                if (j == 0) return i;
            }
            return -1;
        }

        static ConcurrentDictionary<Action,long> reentryDictionary = new ConcurrentDictionary<Action, long>();

        public static void ProtectReentry(Action action)
        {
            long m_InFunction = reentryDictionary.ContainsKey(action) ? reentryDictionary[action] : 0;

            if (Interlocked.CompareExchange(ref m_InFunction, 1, 0) == 0)
            {
                // We're not in the function
                try
                {
                    action();
                }
                finally
                {
                    long temp;
                    reentryDictionary.TryRemove(action, out temp);
                }
            }
            else
            {
                // We're already in the function
            }
        }

        public static int toUnixTime(this DateTime dateTime)
        {
            return (int)dateTime.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
        }

        public static DateTime fromUnixTime(this int time)
        {
            return new DateTime(1970, 1, 1).AddSeconds(time);
        }

        public static double toUnixTimeDouble(this DateTime dateTime)
        {
            return dateTime.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
        }

        public static DateTime fromUnixTime(this double time)
        {
            return new DateTime(1970, 1, 1).AddSeconds(time);
        }

        public static (int degrees, int minutes, float seconds) toDMS(this double angle)
        {
            double degrees = angle;
            double minutes = (degrees - (int) degrees) * 60;
            double seconds = (minutes - (int) minutes) * 60;

            return ((int) degrees, (int)minutes, (float)seconds);
        }

        public static PointLatLngAlt ToPLLA(this PointLatLng pll, double alt)
        {
            return new PointLatLngAlt(pll) { Alt = alt };
        }

        public static double EvaluateMath(this String input)
        {
            if (input == null || input == "")
                return 0;

            String expr = "(" + input + ")";
            Stack<String> ops = new Stack<String>();
            Stack<Double> vals = new Stack<Double>();

            for (int i = 0; i < expr.Length; i++)
            {
                String s = expr.Substring(i, 1);
                if (s.Equals("(")) { }
                else if (s.Equals("+")) ops.Push(s);
                else if (s.Equals("-")) ops.Push(s);
                else if (s.Equals("*")) ops.Push(s);
                else if (s.Equals("/")) ops.Push(s);
                else if (s.Equals("sqrt")) ops.Push(s);
                else if (s.Equals(")"))
                {
                    int count = ops.Count;
                    while (count > 0)
                    {
                        String op = ops.Pop();
                        double v = vals.Pop();
                        if (op.Equals("+")) v = vals.Pop() + v;
                        else if (op.Equals("-")) v = vals.Pop() - v;
                        else if (op.Equals("*")) v = vals.Pop() * v;
                        else if (op.Equals("/")) v = vals.Pop() / v;
                        else if (op.Equals("sqrt")) v = Math.Sqrt(v);
                        vals.Push(v);

                        count--;
                    }
                }
                else vals.Push(Double.Parse(s));
            }
            return vals.Pop();
        }

        public static IEnumerable<byte[]> ReadChunks(this Stream stream, int chunksize = 4096)
        {
            int read = 0;
            var buffer = new byte[chunksize];
            do
            {
                read = stream.Read(buffer, 0, buffer.Length);
                yield return new Span<byte>(buffer, 0, read).ToArray();
            } while (read > 0);
        }

        public static string ToInvariantString(this object obj)
        {
            if (obj != null)
            {
                if (!(obj is DateTime))
                {
                    if (!(obj is DateTimeOffset))
                    {
                        IConvertible c = obj as IConvertible;
                        if (c == null)
                        {
                            IFormattable f = obj as IFormattable;
                            if (f == null)
                            {
                                return obj.ToString();
                            }
                            return f.ToString(null, CultureInfo.InvariantCulture);
                        }
                        return c.ToString(CultureInfo.InvariantCulture);
                    }
                    return ((DateTimeOffset)obj).ToString("o", CultureInfo.InvariantCulture);
                }
                return ((DateTime)obj).ToString("o", CultureInfo.InvariantCulture);
            }
            return null;
        }

        /*
        public static byte[] Compress(this byte[] input)
        {
            return LZ4Pickler.Pickle(input);
        }

        public static byte[] Decompress(this byte[] input)
        {
            return LZ4Pickler.Unpickle(input);
        }
        */
    }
}
