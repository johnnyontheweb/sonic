﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Adletec.Sonic.Util
{
    /// <summary>
    /// Utility methods that can be used throughout the engine.
    /// </summary>
    internal static class EngineUtil
    {
        static internal IDictionary<string, double> ConvertVariableNamesToLowerCase(IDictionary<string, double> variables)
        {
            Dictionary<string, double> temp = new Dictionary<string, double>();
            foreach (KeyValuePair<string, double> keyValuePair in variables)
            {
                string keyL = keyValuePair.Key.ToLowerFast();
                if (!temp.ContainsKey(keyL)) temp.Add(keyL, keyValuePair.Value); else temp[keyL] = keyValuePair.Value;
            }

            return temp;
        }

        // This is a fast ToLower for strings that are in ASCII
        static internal string ToLowerFast(this string text)
        {
            StringBuilder buffer = new StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c >= 'A' && c <= 'Z')
                {
                    buffer.Append((char)(c + 32));
                }
                else
                {
                    buffer.Append(c);
                }
            }

            return buffer.ToString();
        }
    }
}
