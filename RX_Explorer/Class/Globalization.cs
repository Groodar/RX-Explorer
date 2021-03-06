﻿using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.ApplicationModel.Resources;
using Windows.Globalization;
using Windows.Storage;
using Windows.System.UserProfile;

namespace RX_Explorer.Class
{
    /// <summary>
    /// 指示UI语言类型
    /// </summary>
    public static class Globalization
    {
        /// <summary>
        /// 当前使用的语言
        /// </summary>
        public static LanguageEnum CurrentLanguage { get; private set; }

        private static readonly ResourceLoader Loader;
        private static readonly Dictionary<string, string> ResourceCache;

        public static bool SwitchTo(LanguageEnum Language)
        {
            switch (Language)
            {
                case LanguageEnum.Chinese_Simplified:
                    {
                        ApplicationData.Current.LocalSettings.Values["LanguageOverride"] = 0;
                        ApplicationLanguages.PrimaryLanguageOverride = "zh-Hans";
                        break;
                    }
                case LanguageEnum.English:
                    {
                        ApplicationData.Current.LocalSettings.Values["LanguageOverride"] = 1;
                        ApplicationLanguages.PrimaryLanguageOverride = "en-US";
                        break;
                    }
                case LanguageEnum.French:
                    {
                        ApplicationData.Current.LocalSettings.Values["LanguageOverride"] = 2;
                        ApplicationLanguages.PrimaryLanguageOverride = "fr-FR";
                        break;
                    }
                case LanguageEnum.Chinese_Traditional:
                    {
                        ApplicationData.Current.LocalSettings.Values["LanguageOverride"] = 3;
                        ApplicationLanguages.PrimaryLanguageOverride = "zh-Hant";
                        break;
                    }
            }

            return Language != CurrentLanguage;
        }

        public static string GetString(string Key)
        {
            if (ResourceCache.TryGetValue(Key, out string Value))
            {
                return Value;
            }
            else
            {
                try
                {
                    Value = Loader.GetString(Key);

                    if (string.IsNullOrEmpty(Value))
                    {
                        throw new Exception("Value is empty");
                    }
                    else
                    {
                        Value = Value.Replace(@"\r", Environment.NewLine);
                        ResourceCache.Add(Key, Value);
                        return Value;
                    }
                }
                catch
                {
                    throw new Exception("Could not find the key");
                }
            }
        }

        static Globalization()
        {
            if (ApplicationData.Current.LocalSettings.Values["LanguageOverride"] is int LanguageIndex)
            {
                switch (LanguageIndex)
                {
                    case 0:
                        {
                            CurrentLanguage = LanguageEnum.Chinese_Simplified;
                            break;
                        }
                    case 1:
                        {
                            CurrentLanguage = LanguageEnum.English;
                            break;
                        }
                    case 2:
                        {
                            CurrentLanguage = LanguageEnum.French;
                            break;
                        }
                    case 3:
                        {
                            CurrentLanguage = LanguageEnum.Chinese_Traditional;
                            break;
                        }
                }
            }
            else
            {
                string PrimaryLanguage = GlobalizationPreferences.Languages[0];

                if (PrimaryLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                {
                    if(PrimaryLanguage.Contains("Hant"))
                    {
                        CurrentLanguage = LanguageEnum.Chinese_Traditional;
                        ApplicationLanguages.PrimaryLanguageOverride = "zh-Hant";
                        ApplicationData.Current.LocalSettings.Values["LanguageOverride"] = 3;
                    }
                    else if(PrimaryLanguage.Contains("Hans"))
                    {
                        CurrentLanguage = LanguageEnum.Chinese_Simplified;
                        ApplicationLanguages.PrimaryLanguageOverride = "zh-Hans";
                        ApplicationData.Current.LocalSettings.Values["LanguageOverride"] = 0;
                    }
                    else
                    {
                        CurrentLanguage = LanguageEnum.English;
                        ApplicationLanguages.PrimaryLanguageOverride = "en-US";
                        ApplicationData.Current.LocalSettings.Values["LanguageOverride"] = 1;
                    }
                }
                else if (PrimaryLanguage.StartsWith("fr", StringComparison.OrdinalIgnoreCase))
                {
                    CurrentLanguage = LanguageEnum.French;
                    ApplicationLanguages.PrimaryLanguageOverride = "fr-FR";
                    ApplicationData.Current.LocalSettings.Values["LanguageOverride"] = 2;
                }
                else
                {
                    CurrentLanguage = LanguageEnum.English;
                    ApplicationLanguages.PrimaryLanguageOverride = "en-US";
                    ApplicationData.Current.LocalSettings.Values["LanguageOverride"] = 1;
                }
            }

            Loader = ResourceLoader.GetForViewIndependentUse();
            ResourceCache = new Dictionary<string, string>();
        }
    }
}
