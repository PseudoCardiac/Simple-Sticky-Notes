using System;
using System.Collections.Generic;
using System.Drawing;
namespace SimpleStickyNotes
{
    public class FontObject
    {
        //private System.Windows.Media.NoteFontFamily _wpfFamily;
        //public string Name { get; set; } = "Segoe UI";
        //public FontObject(System.Drawing.NoteFontFamily fontFamily)
        public FontObject(System.Drawing.FontFamily fontFamily)
        {
            //_wpfFamily = new(fontFamily.Name);

            //Name = _wpfFamily.FamilyNames[System.Windows.Markup.XmlLanguage.GetLanguage("en-US")].ToString();
            return;
        }

        //public override string ToString()
        //{
        //    return Name;
        //}

        public static string GetFontFamilyName(System.Drawing.FontFamily fontFamily)
        {
            System.Windows.Media.FontFamily wpfFamily = new(fontFamily.Name);
            return wpfFamily.FamilyNames[System.Windows.Markup.XmlLanguage.GetLanguage("en-US")].ToString();
        }
    }
}
