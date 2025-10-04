using System;
/******************************************************************
 * Mapping between anode position and anode number for Nordural
 * 
 Pot Pattern Ranges
    1. “Simple pattern”

    Applies to:

    Line A, pots 1–84

    Line B, pots 1–90

    Line C, pots 1–130

    Line D, pots 1–130

    Rule:
    Each pair of pots (odd/even) shares a base pattern.

    Odd pot:

    Positions 1–10 ? odd anodes (descending, shifted by group index).

    Positions 11–20 ? even anodes (descending, shifted by group index+5).

    Even pot: same, but odd/even halves swapped.

    “Group index” = (potNumber - 1) / 2, then taken modulo 10 for the shift.

    Effect:
    Within each group of 20 anodes, the pattern cycles every 10 pots, producing a repeating diagonal of anodes across pots.

    2. “Complex scrambled pattern”

    Applies to:

    Line A, pots 85–90 (the last six)

    Rule:
    Same idea as simple, but with one half “scrambled” instead of shifted:

    Odd pots in this range:

    Positions 1–10 (odd anodes) are permuted using _oddPermutationMap.

    Positions 11–20 (even anodes) are still shifted.

    Even pots in this range:

    Positions 1–10 (even anodes) are permuted using _evenPermutationMap.

    Positions 11–20 (odd anodes) are still shifted.

    Group index = (potNumber - 1) / 2 - 42, then modulo 10.

    Effect:
    The last few A pots “break” the simple mirror rule — one half is scrambled according to a fixed permutation before applying the cyclic shift.

    3. “Reset after 130”

    Applies to:

    Line C, pots 131–170

    Line D, pots 131–170

    Rule:
    Like the simple pattern, but group index resets at pot 131 instead of continuing.

    Group index = (potNumber - 131) / 2 instead of (potNumber - 1) / 2.

    For D pots >130 only: odd/even pot logic is flipped (odd pots behave like even, even like odd).

    Effect:
    C and D lines are “split” into two contiguous ranges with the same base pattern but separate group counters.
 * 
 **********************************************************/

namespace AnodeMeter
{

    public static class AnodeMapper
    {
        // --- Permutation maps for the complex pattern (A pots > 84) ---
        private static readonly byte[] _oddPermutationMap = { 3, 2, 4, 6, 5, 7, 8, 0, 9, 1 };
        private static readonly byte[] _inverseOddPermutationMap = { 7, 9, 1, 0, 2, 4, 3, 5, 6, 8 };

        private static readonly byte[] _evenPermutationMap = { 8, 7, 9, 1, 0, 2, 3, 5, 4, 6 };
        private static readonly byte[] _inverseEvenPermutationMap = { 4, 3, 5, 6, 8, 7, 9, 1, 0, 2 };

        private static int GetPotNumber(string pot)
        {
            int number = 0;
            // Start from index 1 to skip the character prefix.
            for (int i = 1; i < pot.Length; i++)
            {
                number = number * 10 + (pot[i] - '0');
            }
            return number;
        }

        private static int Mod(int x, int m)
        {
            int r = x % m;
            return r < 0 ? r + m : r;
        }

        public static int GetAnode(string pot, int position)
        {
            char prefix = pot[0];
            int potNumber = GetPotNumber(pot);
            int group;
            bool isPotOdd = (potNumber & 1) != 0;

            // 'A' pots > 84 use the complex pattern.
            if (prefix == 'A' && potNumber > 84)
            {
                group = (potNumber - 1) / 2;

                if (isPotOdd) // Odd Pot: Odd sequence is scrambled, Even is shifted
                {
                    if (position <= 10)
                    {
                        int s_scrambled = Mod(group - 42, 10);
                        int permutedIndex = _oddPermutationMap[position - 1];
                        return 19 - 2 * Mod(permutedIndex + s_scrambled, 10);
                    }
                    else
                    {
                        int s_even = Mod(group + 5, 10);
                        return 20 - 2 * Mod(position - 11 + s_even, 10);
                    }
                }
                else // Even Pot: Even sequence is scrambled, Odd is shifted
                {
                    if (position <= 10)
                    {
                        int s_scrambled = Mod(group - 42, 10);
                        int permutedIndex = _evenPermutationMap[position - 1];
                        return 20 - 2 * Mod(permutedIndex + s_scrambled, 10);
                    }
                    else
                    {
                        int s_odd = Mod(group, 10);
                        return 19 - 2 * Mod(position - 11 + s_odd, 10);
                    }
                }
            }
            else // All other pots use a variation of the simple pattern.
            {
                // For C and D lines, pots > 130 have a reset group number.
                if ((prefix == 'C' || prefix == 'D') && potNumber > 130)
                {
                    group = (potNumber - 131) / 2;
                    // For D line pots > 130, the odd/even logic is flipped.
                    if (prefix == 'D')
                    {
                        isPotOdd = !isPotOdd;
                    }
                }
                else
                {
                    group = (potNumber - 1) / 2;
                }

                int s_odd = Mod(group, 10);
                int s_even = Mod(group + 5, 10);

                if (isPotOdd)
                {
                    return (position <= 10)
                        ? 19 - 2 * Mod(position - 1 + s_odd, 10)
                        : 20 - 2 * Mod(position - 11 + s_even, 10);
                }
                else
                {
                    return (position <= 10)
                        ? 20 - 2 * Mod(position - 1 + s_even, 10)
                        : 19 - 2 * Mod(position - 11 + s_odd, 10);
                }
            }
        }

        public static int GetPosition(string pot, int anode)
        {
            char prefix = pot[0];
            int potNumber = GetPotNumber(pot);
            int group;

            bool isAnodeOdd = (anode & 1) != 0;
            bool isPotOdd = (potNumber & 1) != 0;

            // 'A' pots > 84 use the complex pattern.
            if (prefix == 'A' && potNumber > 84)
            {
                group = (potNumber - 1) / 2;
                if (isPotOdd) // Odd Pot
                {
                    if (isAnodeOdd) // Odd Anode (Scrambled)
                    {
                        int s_scrambled = Mod(group - 42, 10);
                        int j = (19 - anode) / 2;
                        int basePermutedIndex = Mod(j - s_scrambled, 10);
                        return _inverseOddPermutationMap[basePermutedIndex] + 1;
                    }
                    else // Even Anode (Shifted)
                    {
                        int s_even = Mod(group + 5, 10);
                        int j = (20 - anode) / 2;
                        return Mod(j - s_even, 10) + 11;
                    }
                }
                else // Even Pot
                {
                    if (isAnodeOdd) // Odd Anode (Shifted)
                    {
                        int s_odd = Mod(group, 10);
                        int j = (19 - anode) / 2;
                        return Mod(j - s_odd, 10) + 11;
                    }
                    else // Even Anode (Scrambled)
                    {
                        int s_scrambled = Mod(group - 42, 10);
                        int j = (20 - anode) / 2;
                        int basePermutedIndex = Mod(j - s_scrambled, 10);
                        return _inverseEvenPermutationMap[basePermutedIndex] + 1;
                    }
                }
            }
            else // All other pots use a variation of the simple pattern.
            {
                // For C and D lines, pots > 130 have a reset group number.
                if ((prefix == 'C' || prefix == 'D') && potNumber > 130)
                {
                    group = (potNumber - 131) / 2;
                    // For D line pots > 130, the odd/even logic is flipped.
                    if (prefix == 'D')
                    {
                        isPotOdd = !isPotOdd;
                    }
                }
                else
                {
                    group = (potNumber - 1) / 2;
                }

                int s_odd = Mod(group, 10);
                int s_even = Mod(group + 5, 10);
                if (isAnodeOdd)
                {
                    int j = (19 - anode) / 2;
                    int posIndex = Mod(j - s_odd, 10);
                    return isPotOdd ? posIndex + 1 : posIndex + 11;
                }
                else
                {
                    int j = (20 - anode) / 2;
                    int posIndex = Mod(j - s_even, 10);
                    return isPotOdd ? posIndex + 11 : posIndex + 1;
                }
            }
        }
    }
}
