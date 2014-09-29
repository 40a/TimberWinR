﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using Newtonsoft.Json.Linq;
using System.Xml.Linq;
using TimberWinR.Parser;
using RapidRegex.Core;
using System.Text.RegularExpressions;

namespace TimberWinR.Parser
{
    public partial class DateFilter : LogstashFilter
    {                               
        public override bool Apply(JObject json)
        {
            if (!string.IsNullOrEmpty(Type))
            {
                JToken json_type = json["type"];
                if (json_type != null && json_type.ToString() != Type)
                    return true; // Filter does not apply.
            }

            if (Condition != null && !EvaluateCondition(json, Condition))
                return true;

            if (Matches(json))
            {                
                AddFields(json);
            }
           
            return true;
        }

        public override JObject ToJson()
        {
            JObject json = new JObject(
                 new JProperty("date",
                     new JObject(
                         new JProperty("condition", Condition),
                         new JProperty("type", Type),
                         new JProperty("addfields", AddField)                     
                         )));
            return json;
        }

        // copy_field "field1" -> "field2"
        private void AddFields(Newtonsoft.Json.Linq.JObject json)
        {
            string srcField = Match[0];

            if (AddField != null && AddField.Length > 0)
            {
                for (int i = 0; i < AddField.Length; i++)
                {
                    string dstField = ExpandField(AddField[i], json);                 
                    if (json[srcField] != null)
                        AddOrModify(json, dstField, json[srcField]);
                }
            }
        }


        private bool Matches(Newtonsoft.Json.Linq.JObject json)
        {
            string field = Match[0];

            CultureInfo ci = CultureInfo.CreateSpecificCulture(Locale);

            JToken token = null;
            if (json.TryGetValue(field, out token))
            {
                string text = token.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    DateTime ts;
                    var exprArray = Match.Skip(1).ToArray();
                    var resolver = new RegexGrokResolver();
                    for (int i=0; i<exprArray.Length; i++)
                    {
                        var pattern = resolver.ResolveToRegex(exprArray[i]);
                        exprArray[i] = pattern;
                    }
                    if (DateTime.TryParseExact(text, exprArray, ci, DateTimeStyles.None, out ts))
                        AddOrModify(json, ts);                    
                }
                return true; // Empty field is no match
            }
            return false; // Not specified is failure
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
