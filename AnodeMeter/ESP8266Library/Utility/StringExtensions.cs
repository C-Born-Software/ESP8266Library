using System;
using System.Text;

namespace PervasiveDigital.Utilities
{
    public static class StringExtensions
    {
        public static string Replace(this string content, char find, char replace)
        {
            StringBuilder result = new StringBuilder();

            var len = content.Length;
            for (var i = 0 ; i<len ; ++i)
            {
                if (content[i] == find)
                    result.Append(replace);
                else
                    result.Append(content[i]);
            }
            return result.ToString();
        }

        public static string Replace(this string content, string find, string replace)
        {
            int startFrom = 0;
            int findItemLength = find.Length;

            int firstFound = content.IndexOf(find, startFrom);
            StringBuilder returning = new StringBuilder();

            string workingString = content;

            while ((firstFound = workingString.IndexOf(find, startFrom)) >= 0)
            {
                returning.Append(workingString.Substring(0, firstFound));
                returning.Append(replace);

                // the remaining part of the string.
                workingString = workingString.Substring(firstFound + findItemLength, workingString.Length - (firstFound + findItemLength));
            }

            returning.Append(workingString);

            return returning.ToString();
        }

        public static bool Contains(this string source, string check)
        {
            // check string must be smaller
            try
            {
                if (check.Length > source.Length)
                    return false;

                // Now do the easy check
                if (source == check)
                    return true;

                for (int i = 0; i <= (source.Length-check.Length); i++)
                {
                    if (source.Substring(i, check.Length) == check)
                        return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        public static bool StartsWith(this string source, string search)
        {
            if (source == null)
                return false;
            if (search.Length > source.Length)
                return false;
            for (var i = 0; i < search.Length; ++i)
            {
                if (source[i] != search[i])
                    return false;
            }
            return true;
        }

        public static bool EndsWith(this string source, string search)
        {
            if (source == null)
                return false;
            if (search.Length > source.Length)
                return false;

            var iSrc = source.Length-1;            
            for (var i = search.Length-1; i >= 0 ; --i)
            {
                if (source[iSrc] != search[i])
                    return false;
            }
            return true;
        }

        public static string Format(this string value, params string[] args)
        {
            return StringUtilities.Format(value, args);
        }

        public static string[] SplitQuote(this string input)
        {
            string[] tempResult = new string[5];
            int arraySize = 5;
            int currentIndex = 0;

            bool inQuotes = false;
            string currentToken = "";

            for (int i = 0; i < input.Length; i++)
            {
                char ch = input[i];

                if (ch == '"' && !inQuotes)
                {
                    inQuotes = true;
                    currentToken += ch;  // Include the opening quote
                    continue;
                }
                else if (ch == '"' && inQuotes)
                {
                    inQuotes = false;
                    currentToken += ch;  // Include the closing quote
                    continue;
                }

                if (ch == ',' && !inQuotes)
                {
                    tempResult[currentIndex] = currentToken;
                    currentIndex++;

                    if (currentIndex >= arraySize)
                    {
                        arraySize *= 2;
                        string[] newTempResult = new string[arraySize];
                        for (int j = 0; j < currentIndex; j++)
                        {
                            newTempResult[j] = tempResult[j];
                        }
                        tempResult = newTempResult;
                    }

                    currentToken = "";
                }
                else
                {
                    currentToken += ch;
                }
            }

            if (currentToken != "")
            {
                tempResult[currentIndex] = currentToken;
                currentIndex++;
            }

            string[] result = new string[currentIndex];
            for (int i = 0; i < currentIndex; i++)
            {
                result[i] = tempResult[i];
            }
            return result;
        }
    }
}
