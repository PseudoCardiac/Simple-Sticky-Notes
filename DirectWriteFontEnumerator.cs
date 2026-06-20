using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace SimpleStickyNotes;

internal static class DirectWriteFontEnumerator
{
    private static readonly Guid DWriteFactoryGuid = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");

    public static IReadOnlyList<string> GetSystemFontFamilyNames()
    {
        IDWriteFactory? factory = null;
        IDWriteFontCollection? collection = null;

        try
        {
            Marshal.ThrowExceptionForHR(DWriteCreateFactory(DWriteFactoryType.Shared, DWriteFactoryGuid, out factory));
            Marshal.ThrowExceptionForHR(factory.GetSystemFontCollection(out collection, false));

            var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            uint familyCount = collection.GetFontFamilyCount();
            for (uint i = 0; i < familyCount; i++)
            {
                IDWriteFontFamily? family = null;
                IDWriteLocalizedStrings? localizedNames = null;

                try
                {
                    Marshal.ThrowExceptionForHR(collection.GetFontFamily(i, out family));
                    Marshal.ThrowExceptionForHR(family.GetFamilyNames(out localizedNames));

                    string? name = GetBestLocalizedString(localizedNames);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }
                finally
                {
                    ReleaseComObject(localizedNames);
                    ReleaseComObject(family);
                }
            }

            return [.. names];
        }
        finally
        {
            ReleaseComObject(collection);
            ReleaseComObject(factory);
        }
    }

    private static string? GetBestLocalizedString(IDWriteLocalizedStrings strings)
    {
        string[] preferredLocales =
        [
            CultureInfo.CurrentUICulture.Name,
            CultureInfo.CurrentCulture.Name,
            "ko-KR",
            "en-US"
        ];

        foreach (string locale in preferredLocales)
        {
            if (TryGetLocalizedString(strings, locale, out string? value))
            {
                return value;
            }
        }

        return strings.GetCount() > 0 ? GetString(strings, 0) : null;
    }

    private static bool TryGetLocalizedString(IDWriteLocalizedStrings strings, string locale, out string? value)
    {
        value = null;
        Marshal.ThrowExceptionForHR(strings.FindLocaleName(locale, out uint index, out bool exists));
        if (!exists)
        {
            return false;
        }

        value = GetString(strings, index);
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string GetString(IDWriteLocalizedStrings strings, uint index)
    {
        Marshal.ThrowExceptionForHR(strings.GetStringLength(index, out uint length));

        var buffer = new StringBuilder(checked((int)length + 1));
        Marshal.ThrowExceptionForHR(strings.GetString(index, buffer, (uint)buffer.Capacity));
        return buffer.ToString();
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    [DllImport("dwrite.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int DWriteCreateFactory(
        DWriteFactoryType factoryType,
        [MarshalAs(UnmanagedType.LPStruct)] Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IDWriteFactory factory);

    private enum DWriteFactoryType
    {
        Shared = 0,
        Isolated = 1
    }

    [ComImport]
    [Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDWriteFactory
    {
        [PreserveSig]
        int GetSystemFontCollection(
            [MarshalAs(UnmanagedType.Interface)] out IDWriteFontCollection fontCollection,
            [MarshalAs(UnmanagedType.Bool)] bool checkForUpdates);
    }

    [ComImport]
    [Guid("a84cee02-3eea-4eee-a827-87c1a02a0fcc")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDWriteFontCollection
    {
        uint GetFontFamilyCount();

        [PreserveSig]
        int GetFontFamily(
            uint index,
            [MarshalAs(UnmanagedType.Interface)] out IDWriteFontFamily fontFamily);
    }

    [ComImport]
    [Guid("da20d8ef-812a-4c43-9802-62ec4abd7add")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDWriteFontFamily
    {
        [PreserveSig]
        int GetFontCollection(IntPtr fontCollection);

        uint GetFontCount();

        [PreserveSig]
        int GetFont(uint index, IntPtr font);

        [PreserveSig]
        int GetFamilyNames(
            [MarshalAs(UnmanagedType.Interface)] out IDWriteLocalizedStrings names);
    }

    [ComImport]
    [Guid("08256209-099a-4b34-b86d-c22b110e7771")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDWriteLocalizedStrings
    {
        uint GetCount();

        [PreserveSig]
        int FindLocaleName(
            [MarshalAs(UnmanagedType.LPWStr)] string localeName,
            out uint index,
            [MarshalAs(UnmanagedType.Bool)] out bool exists);

        [PreserveSig]
        int GetLocaleNameLength(uint index, out uint length);

        [PreserveSig]
        int GetLocaleName(
            uint index,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder localeName,
            uint size);

        [PreserveSig]
        int GetStringLength(uint index, out uint length);

        [PreserveSig]
        int GetString(
            uint index,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder stringBuffer,
            uint size);
    }
}
