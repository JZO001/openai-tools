// @author: Devis Lucato. @license: CC0.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Forge.OpenAI.GPT.Settings;

namespace Forge.OpenAI.GPT
{
    public static class GPT3Tokenizer
    {
        private static readonly ConcurrentDictionary<string, string> BPE_CACHE = new ConcurrentDictionary<string, string>();
        private static Dictionary<int, char>? BYTES_TO_UNICODE_CACHE;

        public static List<int> Encode(string text)
        {
            if (string.IsNullOrEmpty(text)) return new List<int>();
            var byteEncoder = BytesToUnicode();

            var pat = @"'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+";
            var matches = Regex.Matches(text, pat);

            var bpeTokens = new List<int>();
            foreach (Match? match in matches)
            {
                var token = new string(Encoding.UTF8.GetBytes(match!.Value).Select(x => byteEncoder[x]).ToArray());
                var newTokens = BytePairEncoding(token).Split(' ').Select(x => GPT3Settings.Encoder[x]).ToList();
                bpeTokens.AddRange(newTokens);
            }

            return bpeTokens;
        }

        public static List<int> Encode(StringBuilder? stringBuilder)
        {
            return stringBuilder == null ? new List<int>() : Encode(stringBuilder.ToString());
        }

        public static List<int> Encode(char[]? chars)
        {
            return chars == null ? new List<int>() : Encode(new string(chars));
        }

        public static List<int> Encode(IEnumerable<char>? chars)
        {
            return chars == null ? new List<int>() : Encode(chars.ToArray());
        }

        private static int Ord(string x) => char.ConvertToUtf32(x, 0);

        private static Dictionary<int, char> BytesToUnicode()
        {
            // Note: no visible gain with this
            if (BYTES_TO_UNICODE_CACHE != null) return BYTES_TO_UNICODE_CACHE;

            var bytes = Enumerable.Range(Ord("!"), Ord("~") + 1 - Ord("!"))
                .Concat(Enumerable.Range(Ord("¡"), Ord("¬") + 1 - Ord("¡")))
                .Concat(Enumerable.Range(Ord("®"), Ord("ÿ") + 1 - Ord("®")))
                .ToList();

            var chars = (from x in bytes select (char)x).ToList();

            var n = 0;
            for (var b = 0; b < 256; b++)
            {
                if (bytes.Contains(b)) continue;
                bytes.Add(b);
                chars.Add((char)(256 + n++));
            }

            BYTES_TO_UNICODE_CACHE = bytes
                .Zip(chars, (k, v) => new { k, v })
                .ToDictionary(x => x.k, x => x.v);

            return BYTES_TO_UNICODE_CACHE;
        }

        private static string BytePairEncoding(string token)
        {
            if (BPE_CACHE.ContainsKey(token)) return BPE_CACHE[token];

            var word = (from x in token.ToList() select x.ToString()).ToList();
            var pairs = GetPairs(word);
            if (pairs.Count == 0)
            {
                BPE_CACHE.TryAdd(token, token);
                return token;
            }

            while (true)
            {
                var minPairs = new SortedDictionary<long, Tuple<string, string>>();
                foreach (var pair in pairs)
                {
                    if (GPT3Settings.BpeRanks.ContainsKey(pair))
                    {
                        var rank = GPT3Settings.BpeRanks[pair];
                        minPairs[rank] = pair;
                    }
                    else
                    {
                        minPairs[100000000000] = pair;
                    }
                }

                var biGram = minPairs[minPairs.Keys.Min()];
                if (!GPT3Settings.BpeRanks.ContainsKey(biGram)) break;

                var first = biGram.Item1;
                var second = biGram.Item2;

                var newWord = new List<string>();
                var i = 0;

                while (i < word.Count)
                {
                    var j = word.IndexOf(first, i);

                    if (j == -1)
                    {
                        var slice = new ArraySegment<string>((from x in word select x.ToString()).ToArray(), i, word.Count - i);
                        newWord.AddRange(slice);
                        break;
                    }

                    var slice2 = new ArraySegment<string>((from x in word select x.ToString()).ToArray(), i, j - i);
                    newWord.AddRange(slice2);
                    i = j;

                    if (word[i] == first && i < word.Count - 1 && word[i + 1] == second)
                    {
                        newWord.Add($"{first}{second}");
                        i += 2;
                    }
                    else
                    {
                        newWord.Add(word[i]);
                        i += 1;
                    }
                }

                word = newWord;
                if (word.Count == 1) break;
                pairs = GetPairs(word);
            }

            var result = string.Join(" ", word);
            BPE_CACHE.TryAdd(token, result);
            return result;
        }

        /// <summary>
        /// Return set of symbol pairs in a word.
        /// </summary>
        private static List<Tuple<string, string>> GetPairs(List<string> word)
        {
            var result = new List<Tuple<string, string>>();

            var prevChar = word[0];
            for (var i = 1; i < word.Count; i++)
            {
                var currentChar = word[i];
                result.Add(new Tuple<string, string>(prevChar, currentChar));
                prevChar = currentChar;
            }

            return result;
        }
    }
}
