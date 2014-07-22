﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Xml.Linq;

namespace TimberWinR.Filters
{
    public class DateFilter : FilterBase
    {
        public string Field { get; private set; }
        public string Target { get; private set; }
        public bool ConvertToUTC { get; private set; }
        public List<string> Patterns { get; private set; }

        public static void Parse(List<FilterBase> filters, XElement dateElement)
        {
            filters.Add(parseDate(dateElement));
        }

        static DateFilter parseDate(XElement e)
        {
            return new DateFilter(e);
        }

        DateFilter(XElement parent)
        {
            Patterns = new List<string>();

            ParseField(parent);
            ParseTarget(parent);
            ParseConvertToUTC(parent);
            ParsePatterns(parent);
        }

        private void ParseField(XElement parent)
        {
            string attributeName = "field";

            try
            {
                XAttribute a = parent.Attribute(attributeName);
                Field = a.Value;
            }
            catch
            {
            }
        }

        private void ParseTarget(XElement parent)
        {
            string attributeName = "field";

            try
            {
                XAttribute a = parent.Attribute(attributeName);
                Field = a.Value;
            }
            catch
            {
            }
        }

        private void ParseConvertToUTC(XElement parent)
        {
            string attributeName = "convertToUTC";
            string value;

            try
            {
                XAttribute a = parent.Attribute(attributeName);

                value = a.Value;

                if (value == "ON" || value == "true")
                {
                    ConvertToUTC = true;
                }
                else if (value == "OFF" || value == "false")
                {
                    ConvertToUTC = false;
                }
                else
                {
                    throw new TimberWinR.ConfigurationErrors.InvalidAttributeValueException(parent.Attribute(attributeName));
                }
            }
            catch { }
        }

        private void ParsePatterns(XElement parent)
        {
            foreach (var e in parent.Elements("Pattern"))
            {
                string pattern = e.Value;
                Patterns.Add(pattern);
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("DateFilter\n");
            foreach (var prop in this.GetType().GetProperties())
            {
                if (prop != null)
                {
                    sb.Append(String.Format("\t{0}: {1}\n", prop.Name, prop.GetValue(this, null)));
                }

            }
            return sb.ToString();
        }

        public override void Apply(JObject json)
        {
            JToken token = null;
            if (json.TryGetValue(Field, StringComparison.OrdinalIgnoreCase, out token))
            {
                string text = token.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    DateTime ts;
                    if (Patterns == null || Patterns.Count == 0)
                    {
                        if (DateTime.TryParse(text, out ts))
                            AddOrModify(json, ts);
                    }
                    else
                    {
                        if (DateTime.TryParseExact(text, Patterns.ToArray(), CultureInfo.InvariantCulture, DateTimeStyles.None, out ts))
                            AddOrModify(json, ts);
                    }
                }
            }
        }


        private void AddOrModify(JObject json, DateTime ts)
        {
            if (ConvertToUTC)
                ts = ts.ToUniversalTime();

            if (json[Target] == null)
                json.Add(Target, ts);
            else
                json[Target] = ts;
        }

    }
}
