using System;
using System.Collections.Generic;
using System.Linq;

#nullable disable

namespace Chummer
{
    /// <summary>
    /// Pure SR5 cyberlimb arithmetic shared by Character/Cyberware and the portable
    /// source adapter. Callers supply evaluated, authoritative inputs. No XML,
    /// saved total, source selection, or mutation belongs in this class.
    /// </summary>
    public static class PortableCyberlimbAttributeRules
    {
        public const string Revision = "sr5-cyberlimb-arithmetic-selection/3935-v1";
        public const int DefaultBaseAttribute = 3;
        public const int DefaultSlotCount = 1;

        public static int InheritedValue(int positiveChildTotal, int positiveChildCount)
            => positiveChildTotal / Math.Max(positiveChildCount, 1);

        public static int AttributeValue(int customizedOrBaseValue, int naturalMaximum)
            => Math.Min(customizedOrBaseValue, naturalMaximum);

        public static int AttributeTotal(int attributeValue, int bonus, int bonusCap, int augmentedMaximum)
            => Math.Min(attributeValue + Math.Min(bonus, bonusCap), augmentedMaximum);

        public static int CharacterLimbCount(int profileCount, string slot, int addedLimbs)
            => (string.IsNullOrEmpty(slot) ? profileCount : slot == "arm" || slot == "leg" ? 2 : 1) + addedLimbs;

        public static int WeightedTotal(int naturalTotal, int cyberTotal, int replacedCount, int characterCount)
            => (cyberTotal + Math.Max(naturalTotal, 0) * Math.Max(characterCount - replacedCount, 0)
                + characterCount - 1) / characterCount;

        public static bool CyberlimbsAllowZero(int replacedCount, int pureCyberTotal)
            => replacedCount > 0 && pureCyberTotal == 0;

        public static bool ApplyParentMount(bool equipped, bool parentHasMount, bool parentPlugsIntoMount)
            => !parentPlugsIntoMount && (parentHasMount || equipped);

        public static int RedlinerBonus(int countedLimbs, bool affectsStrengthOrAgility)
            => affectsStrengthOrAgility ? Math.Min(countedLimbs / 2, 2) : 0;
    }

    /// <summary>
    /// Canonical MetaValueOf selection after enabled/condition/type/name filtering.
    /// Both engine cache population and the portable adapter call this code.
    /// The custom pass intentionally retains the engine's used-row behavior:
    /// custom unique winners attach to an existing ordinary-name list, and do
    /// not create used-row membership when that ordinary-name list is absent.
    /// Do not substitute Value != 0 or input-row existence for Used membership.
    /// </summary>
    public static class PortableImprovementSelection
    {
        public static void Select<T>(List<T> lstImprovementsToConsider,
            Func<T, decimal> funcValueGetter, Func<T, string> improvedName,
            Func<T, string> uniqueName, Func<T, bool> isCustom,
            out Dictionary<string, decimal> values, out Dictionary<string, List<T>> used)
            where T : class
        {
            List<T> lstLoopImprovements;
            Dictionary<string, List<ValueTuple<string, T>>> dicUniquePairs
                = new Dictionary<string, List<ValueTuple<string, T>>>(lstImprovementsToConsider.Count);
            Dictionary<string, decimal> dicValues
                = new Dictionary<string, decimal>(lstImprovementsToConsider.Count);
            Dictionary<string, List<T>> dicImprovementsForValues
                = new Dictionary<string, List<T>>(lstImprovementsToConsider.Count);
            Dictionary<string, HashSet<string>> dicUniqueNames
                = new Dictionary<string, HashSet<string>>(lstImprovementsToConsider.Count);
            try
            {
                foreach (T objImprovement in lstImprovementsToConsider)
                {
                    // Count custom improvements later
                    if (isCustom(objImprovement))
                        continue;
                    string strLoopImprovedName = improvedName(objImprovement);
                    string strUniqueName = uniqueName(objImprovement);
                    if (!string.IsNullOrEmpty(strUniqueName))
                    {
                        // If this has a UniqueName, run through the current list of UniqueNames seen. If it is not already in the list, add it.
                        if (dicUniqueNames.TryGetValue(strLoopImprovedName, out HashSet<string> setUniqueNames))
                        {
                            setUniqueNames.Add(strUniqueName);
                        }
                        else
                        {
                            HashSet<string> setTemp = new HashSet<string>();
                            setTemp.Add(strUniqueName);
                            dicUniqueNames.Add(strLoopImprovedName, setTemp);
                        }

                        // Add the values to the UniquePair List so we can check them later.
                        if (dicUniquePairs.TryGetValue(strLoopImprovedName,
                                                       out List<ValueTuple<string, T>> lstUniquePairs))
                        {
                            lstUniquePairs.Add(new ValueTuple<string, T>(strUniqueName, objImprovement));
                        }
                        else
                        {
                            dicUniquePairs.Add(strLoopImprovedName,
                                               new List<ValueTuple<string, T>>(lstImprovementsToConsider.Count)
                                               {
                                                   new ValueTuple<string, T>(strUniqueName, objImprovement)
                                               });
                        }

                        if (!dicValues.ContainsKey(strLoopImprovedName))
                        {
                            dicValues.Add(strLoopImprovedName, 0);
                            dicImprovementsForValues.Add(strLoopImprovedName, new List<T>(lstImprovementsToConsider.Count));
                        }
                    }
                    else if (dicValues.TryGetValue(strLoopImprovedName, out decimal decExistingValue))
                    {
                        dicValues[strLoopImprovedName] = decExistingValue + funcValueGetter(objImprovement);
                        dicImprovementsForValues[strLoopImprovedName].Add(objImprovement);
                    }
                    else
                    {
                        dicValues.Add(strLoopImprovedName, funcValueGetter(objImprovement));
                        dicImprovementsForValues.Add(strLoopImprovedName,
                                                     new List<T>(lstImprovementsToConsider.Count) { objImprovement });
                    }
                }

                List<T> lstInnerLoopImprovements = new List<T>(lstImprovementsToConsider.Count);
                foreach (KeyValuePair<string, HashSet<string>> objLoopValuePair in dicUniqueNames)
                {
                    string strLoopImprovedName = objLoopValuePair.Key;
                    bool blnValuesDictionaryContains
                        = dicValues.TryGetValue(strLoopImprovedName, out decimal decLoopValue);
                    if (blnValuesDictionaryContains)
                        dicImprovementsForValues.TryGetValue(strLoopImprovedName, out lstLoopImprovements);
                    else
                        lstLoopImprovements = new List<T>(dicUniqueNames.Count);
                    if (dicUniquePairs.TryGetValue(strLoopImprovedName,
                                                   out List<ValueTuple<string, T>> lstUniquePairs))
                    {
                        HashSet<string> setUniqueNames = objLoopValuePair.Value;
                        lstInnerLoopImprovements.Clear();
                        if (setUniqueNames.Contains("precedence0"))
                        {
                            // Retrieve only the highest precedence0 value.
                            // Run through the list of UniqueNames and pick out the highest value for each one.
                            T objHighestImprovement = null;
                            decimal decHighest = decimal.MinValue;
                            foreach ((string strUnique, T objLoopImprovement) in lstUniquePairs)
                            {
                                if (strUnique != "precedence0")
                                    continue;
                                decimal decInnerLoopValue = funcValueGetter(objLoopImprovement);
                                if (decHighest < decInnerLoopValue)
                                {
                                    decHighest = decInnerLoopValue;
                                    objHighestImprovement = objLoopImprovement;
                                }
                            }

                            if (objHighestImprovement != null)
                                lstInnerLoopImprovements.Add(objHighestImprovement);

                            if (setUniqueNames.Contains("precedence-1"))
                            {
                                foreach ((string strUnique, T objLoopImprovement) in lstUniquePairs)
                                {
                                    if (strUnique != "precedence-1")
                                        continue;
                                    decHighest += funcValueGetter(objLoopImprovement);
                                    lstInnerLoopImprovements.Add(objLoopImprovement);
                                }
                            }

                            if (decLoopValue < decHighest)
                            {
                                decLoopValue = decHighest;
                                lstLoopImprovements.Clear();
                                lstLoopImprovements.AddRange(lstInnerLoopImprovements);
                            }
                        }
                        else if (setUniqueNames.Contains("precedence1"))
                        {
                            // Retrieve all the items that are precedence1 and nothing else.
                            decimal decHighest = 0;
                            foreach ((string strUnique, T objLoopImprovement) in lstUniquePairs)
                            {
                                if (strUnique != "precedence1" && strUnique != "precedence-1")
                                    continue;
                                decHighest += funcValueGetter(objLoopImprovement);
                                lstInnerLoopImprovements.Add(objLoopImprovement);
                            }

                            if (decLoopValue < decHighest)
                            {
                                decLoopValue = decHighest;
                                lstLoopImprovements.Clear();
                                lstLoopImprovements.AddRange(lstInnerLoopImprovements);
                            }
                        }
                        else
                        {
                            // Run through the list of UniqueNames and pick out the highest value for each one.
                            foreach (string strUniqueName in setUniqueNames)
                            {
                                T objHighestImprovement = null;
                                decimal decHighest = decimal.MinValue;
                                foreach ((string strUnique, T objLoopImprovement) in lstUniquePairs)
                                {
                                    if (strUnique != strUniqueName)
                                        continue;
                                    decimal decInnerLoopValue = funcValueGetter(objLoopImprovement);
                                    if (decHighest < decInnerLoopValue)
                                    {
                                        decHighest = decInnerLoopValue;
                                        objHighestImprovement = objLoopImprovement;
                                    }
                                }

                                if (decHighest != decimal.MinValue)
                                {
                                    decLoopValue += decHighest;
                                    lstLoopImprovements.Add(objHighestImprovement);
                                }
                            }
                        }

                        if (blnValuesDictionaryContains)
                            dicValues[strLoopImprovedName] = decLoopValue;
                        else
                        {
                            dicValues.Add(strLoopImprovedName, decLoopValue);
                            dicImprovementsForValues.Add(strLoopImprovedName, lstLoopImprovements);
                        }
                    }
                }
            }
            finally
            {
                List<HashSet<string>> lstToReturn = dicUniqueNames.Values.ToList();
                for (int i = lstToReturn.Count - 1; i >= 0; --i)
                {
                    HashSet<string> setLoop = lstToReturn[i];
                    setLoop.Clear();
                }
            }

            // Factor in Custom Improvements.
            dicUniqueNames.Clear();
            dicUniquePairs.Clear();
            Dictionary<string, decimal> dicCustomValues
                = new Dictionary<string, decimal>(lstImprovementsToConsider.Count);
            Dictionary<string, List<T>> dicCustomImprovementsForValues
                = new Dictionary<string, List<T>>(lstImprovementsToConsider.Count);
            try
            {
                foreach (T objImprovement in lstImprovementsToConsider)
                {
                    if (!isCustom(objImprovement))
                        continue;
                    string strLoopImprovedName = improvedName(objImprovement);
                    string strUniqueName = uniqueName(objImprovement);
                    if (!string.IsNullOrEmpty(strUniqueName))
                    {
                        // If this has a UniqueName, run through the current list of UniqueNames seen. If it is not already in the list, add it.
                        if (dicUniqueNames.TryGetValue(strLoopImprovedName, out HashSet<string> setUniqueNames))
                        {
                            setUniqueNames.Add(strUniqueName);
                        }
                        else
                        {
                            HashSet<string> setTemp = new HashSet<string>();
                            setTemp.Add(strUniqueName);
                            dicUniqueNames.Add(strLoopImprovedName, setTemp);
                        }

                        // Add the values to the UniquePair List so we can check them later.
                        if (dicUniquePairs.TryGetValue(strLoopImprovedName,
                                                       out List<ValueTuple<string, T>> lstUniquePairs))
                        {
                            lstUniquePairs.Add(new ValueTuple<string, T>(strUniqueName, objImprovement));
                        }
                        else
                        {
                            dicUniquePairs.Add(strLoopImprovedName,
                                               new List<ValueTuple<string, T>>(lstImprovementsToConsider.Count)
                                               {
                                                   new ValueTuple<string, T>(strUniqueName, objImprovement)
                                               });
                        }

                        if (!dicCustomValues.ContainsKey(strLoopImprovedName))
                        {
                            dicCustomValues.Add(strLoopImprovedName, 0);
                            dicCustomImprovementsForValues.Add(strLoopImprovedName, new List<T>(lstImprovementsToConsider.Count));
                        }
                    }
                    else if (dicCustomValues.TryGetValue(strLoopImprovedName, out decimal decExistingValue))
                    {
                        dicCustomValues[strLoopImprovedName]
                            = decExistingValue + funcValueGetter(objImprovement);
                        dicCustomImprovementsForValues[strLoopImprovedName].Add(objImprovement);
                    }
                    else
                    {
                        dicCustomValues.Add(strLoopImprovedName, funcValueGetter(objImprovement));
                        dicCustomImprovementsForValues.Add(strLoopImprovedName,
                                                           new List<T>(lstImprovementsToConsider.Count) { objImprovement });
                    }
                }

                foreach (KeyValuePair<string, HashSet<string>> objLoopValuePair in dicUniqueNames)
                {
                    string strLoopImprovedName = objLoopValuePair.Key;
                    bool blnValuesDictionaryContains
                        = dicCustomValues.TryGetValue(strLoopImprovedName, out decimal decLoopValue);
                    if (blnValuesDictionaryContains)
                        dicImprovementsForValues.TryGetValue(strLoopImprovedName, out lstLoopImprovements);
                    else
                        lstLoopImprovements = new List<T>(dicUniqueNames.Count);
                    if (dicUniquePairs.TryGetValue(strLoopImprovedName,
                                                   out List<ValueTuple<string, T>> lstUniquePairs))
                    {
                        // Run through the list of UniqueNames and pick out the highest value for each one.
                        foreach (string strUniqueName in objLoopValuePair.Value)
                        {
                            T objHighestImprovement = null;
                            decimal decHighest = decimal.MinValue;
                            foreach ((string strUnique, T objLoopImprovement) in lstUniquePairs)
                            {
                                if (strUnique != strUniqueName)
                                    continue;
                                decimal decInnerLoopValue = funcValueGetter(objLoopImprovement);
                                if (decHighest < decInnerLoopValue)
                                {
                                    decHighest = decInnerLoopValue;
                                    objHighestImprovement = objLoopImprovement;
                                }
                            }

                            if (decHighest != decimal.MinValue)
                            {
                                decLoopValue += decHighest;
                                (lstLoopImprovements ?? (lstLoopImprovements = new List<T>(lstImprovementsToConsider.Count))).Add(
                                    objHighestImprovement);
                            }
                        }

                        if (blnValuesDictionaryContains)
                            dicCustomValues[strLoopImprovedName] = decLoopValue;
                        else
                        {
                            dicCustomValues.Add(strLoopImprovedName, decLoopValue);
                            dicCustomImprovementsForValues.Add(strLoopImprovedName, lstLoopImprovements);
                        }
                    }
                }
            }
            finally
            {
                List<HashSet<string>> lstToReturn = dicUniqueNames.Values.ToList();
                for (int i = lstToReturn.Count - 1; i >= 0; --i)
                {
                    HashSet<string> setLoop = lstToReturn[i];
                    setLoop.Clear();
                }
            }

            foreach (KeyValuePair<string, decimal> objLoopValuePair in dicCustomValues)
            {
                string strLoopImprovedName = objLoopValuePair.Key;
                if (dicValues.TryGetValue(strLoopImprovedName, out decimal decExistingValue))
                {
                    dicValues[strLoopImprovedName] = decExistingValue + objLoopValuePair.Value;
                    dicImprovementsForValues[strLoopImprovedName]
                        .AddRange(dicCustomImprovementsForValues[strLoopImprovedName]);
                }
                else
                {
                    dicValues.Add(strLoopImprovedName, objLoopValuePair.Value);
                    dicImprovementsForValues.Add(strLoopImprovedName,
                                                 dicCustomImprovementsForValues[strLoopImprovedName]);
                }
            }

            values = dicValues;
            used = dicImprovementsForValues;
        }
    }
}
