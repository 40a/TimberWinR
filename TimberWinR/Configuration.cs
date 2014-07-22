﻿using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.IO;
using System.Globalization;
using TimberWinR.Inputs;
using TimberWinR.Filters;
using System.Xml.Schema;
using NLog;

namespace TimberWinR
{
    public class Configuration
    {

        private class MissingRequiredTagException : Exception
        {
            public MissingRequiredTagException(string tagName)
                : base(
                    string.Format("Missing required tag \"{0}\"", tagName))
            {
            }
        }

        private class MissingRequiredAttributeException : Exception
        {
            public MissingRequiredAttributeException(XElement e, string attributeName)
                : base(
                    string.Format("{0}:{1} Missing required attribute \"{2}\" for element <{3}>", e.Document.BaseUri,
                        ((IXmlLineInfo) e).LineNumber, attributeName, e.Name.ToString()))
            {
            }
        }

        private class InvalidAttributeNameException : Exception
        {
            public InvalidAttributeNameException(XAttribute a)
                : base(
                    string.Format("{0}:{1} Invalid Attribute Name <{2} {3}>", a.Document.BaseUri,
                        ((IXmlLineInfo) a).LineNumber, a.Parent.Name, a.Name.ToString()))
            {
            }
        }

        private class InvalidAttributeDateValueException : Exception
        {
            public InvalidAttributeDateValueException(XAttribute a)
                : base(
                    string.Format(
                        "{0}:{1} Invalid date format given for attribute. Format must be \"yyyy-MM-dd hh:mm:ss\". <{2} {3}>",
                        a.Document.BaseUri,
                        ((IXmlLineInfo) a).LineNumber, a.Parent.Name, a.ToString()))
            {
            }
        }

        private class InvalidAttributeIntegerValueException : Exception
        {
            public InvalidAttributeIntegerValueException(XAttribute a)
                : base(
                    string.Format("{0}:{1} Integer value not given for attribute. <{2} {3}>", a.Document.BaseUri,
                        ((IXmlLineInfo) a).LineNumber, a.Parent.Name, a.ToString()))
            {
            }
        }

        private class InvalidAttributeValueException : Exception
        {
            public InvalidAttributeValueException(XAttribute a)
                : base(
                    string.Format("{0}:{1} Invalid Attribute Value <{2} {3}>", a.Document.BaseUri,
                        ((IXmlLineInfo) a).LineNumber, a.Parent.Name, a.ToString()))
            {
            }
        }

        private class InvalidElementNameException : Exception
        {
            public InvalidElementNameException(XElement e)
                : base(
                    string.Format("{0}:{1} Invalid Element Name <{2}> <{3}>", e.Document.BaseUri,
                        ((IXmlLineInfo) e).LineNumber, e.Parent.Name, e.ToString()))
            {
            }
        }

        private static List<WindowsEvent> _events = new List<WindowsEvent>();

        public IEnumerable<WindowsEvent> Events
        {
            get { return _events; }
        }

        private static List<TailFileInput> _logs = new List<TailFileInput>();

        public IEnumerable<TailFileInput> Logs
        {
            get { return _logs; }
        }

        private static List<IISLog> _iislogs = new List<IISLog>();

        public IEnumerable<IISLog> IIS
        {
            get { return _iislogs; }
        }

        private static List<IISW3CLog> _iisw3clogs = new List<IISW3CLog>();

        public IEnumerable<IISW3CLog> IISW3C
        {
            get { return _iisw3clogs; }
        }

        private static List<FilterBase> _filters = new List<FilterBase>();

        public IEnumerable<FilterBase> Filters
        {
            get { return _filters; }
        }

        public Configuration(string xmlConfFile)
        {
            validateWithSchema(xmlConfFile, Properties.Resources.configSchema);

            parseConfInput(xmlConfFile);
            parseConfFilter(xmlConfFile);
        }

        private static void validateWithSchema(string xmlConfFile, string xsdSchema)
        {
            XDocument config = XDocument.Load(xmlConfFile, LoadOptions.SetLineInfo | LoadOptions.SetBaseUri);

            // Ensure that the xml configuration file provided obeys the xsd schema.
            XmlSchemaSet schemas = new XmlSchemaSet();
            schemas.Add("", XmlReader.Create(new StringReader(xsdSchema)));
            bool errorsFound = false;
            config.Validate(schemas, (o, e) =>
            {
                errorsFound = true;
                LogManager.GetCurrentClassLogger().Error(e.Message);
            }, true);

            if (errorsFound)          
                DumpInvalidNodes(config.Root);            
        }

        static void DumpInvalidNodes(XElement el)
        {
            if (el.GetSchemaInfo().Validity != XmlSchemaValidity.Valid)
                LogManager.GetCurrentClassLogger().Error("Invalid Element {0}",
                    el.AncestorsAndSelf()
                    .InDocumentOrder()
                    .Aggregate("", (s, i) => s + "/" + i.Name.ToString()));
            foreach (XAttribute att in el.Attributes())
                if (att.GetSchemaInfo().Validity != XmlSchemaValidity.Valid)
                    LogManager.GetCurrentClassLogger().Error("Invalid Attribute {0}",
                        att
                        .Parent
                        .AncestorsAndSelf()
                        .InDocumentOrder()
                        .Aggregate("",
                            (s, i) => s + "/" + i.Name.ToString()) + "/@" + att.Name.ToString()
                        );
            foreach (XElement child in el.Elements())
                DumpInvalidNodes(child);
        }


        static void parseConfInput(string xmlConfFile)
        {
            XDocument config = XDocument.Load(xmlConfFile, LoadOptions.SetLineInfo | LoadOptions.SetBaseUri);

            // Begin parsing the xml configuration file.
            IEnumerable<XElement> inputs =
                from el in config.Root.Elements("Inputs")
                select el;
            Dictionary<string, Type> allPossibleFields = new Dictionary<string, Type>()
            {
                { "EventLog", typeof(string) },
                { "RecordNumber", typeof(int) },
                { "TimeGenerated", typeof(DateTime) },
                { "TimeWritten", typeof(DateTime) },
                { "EventID", typeof(int) },
                { "EventType", typeof(int) },
                { "EventTypeName", typeof(string) },
                { "EventCategory", typeof(int) },
                { "EventCategoryName", typeof(string) },
                { "SourceName", typeof(string) },
                { "Strings", typeof(string) },
                { "ComputerName", typeof(string) },
                { "SID", typeof(string) },
                { "Message", typeof(string) },
                { "Data", typeof(string) }
            };

            string tagName = "Inputs";
            if (inputs.Count() == 0)
            {
                throw new MissingRequiredTagException(tagName);
            }

            // WINDOWS EVENTS
            IEnumerable<XElement> xml_events =
                from el in inputs.Elements("WindowsEvents").Elements("Event")
                select el;

            foreach (XElement e in xml_events)
            {
                // Required attributes.
                string source;


                // Parse required attributes.
                string attributeName;

                attributeName = "source";
                try
                {
                    source = e.Attribute("source").Value;
                }
                catch (NullReferenceException)
                {
                    throw new MissingRequiredAttributeException(e, attributeName);
                }

                // Parse fields.
                IEnumerable<XElement> xml_fields =
                    from el in e.Elements("Fields").Elements("Field")
                    select el;

                List<FieldDefinition> fields = parseFields(xml_fields, allPossibleFields);

                // Parse parameters.
                Params_WindowsEvent args = parseParams_Event(e.Attributes());

                WindowsEvent evt = new WindowsEvent(source, fields, args);
                _events.Add(evt);
            }



            // TEXT LOGS
            IEnumerable<XElement> xml_logs =
                from el in inputs.Elements("Logs").Elements("Log")
                select el;

            allPossibleFields = new Dictionary<string, Type>()
            {
                { "LogFilename", typeof(string) },
                { "Index", typeof(int) },
                { "Text", typeof(string) }
            };

            foreach (XElement e in xml_logs)
            {
                // Required attributes.
                string name;
                string location;


                // Parse required attributes.
                string attributeName;

                attributeName = "name";
                try
                {
                    name = e.Attribute(attributeName).Value;
                }
                catch (NullReferenceException)
                {
                    throw new MissingRequiredAttributeException(e, attributeName);
                }

                attributeName = "location";
                try
                {
                    location = e.Attribute("location").Value;
                }
                catch (NullReferenceException)
                {
                    throw new MissingRequiredAttributeException(e, attributeName);
                }

                // Parse fields.
                IEnumerable<XElement> xml_fields =
                    from el in e.Elements("Fields").Elements("Field")
                    select el;
                List<FieldDefinition> fields = parseFields(xml_fields, allPossibleFields);

                // Parse parameters.
                Params_TextLog args = parseParams_Log(e.Attributes());

                TailFileInput log = new TailFileInput(name, location, fields, args);
                _logs.Add(log);
            }



            // IIS LOGS
            IEnumerable<XElement> xml_iis =
                from el in inputs.Elements("IISLogs").Elements("IISLog")
                select el;
            allPossibleFields = new Dictionary<string, Type>()
            {
                { "LogFilename", typeof(string) },
                { "LogRow", typeof(int) },
                { "UserIP", typeof(string) },
                { "UserName", typeof(string) },
                { "Date", typeof(DateTime) },
                { "Time", typeof(DateTime) },
                { "ServiceInstance", typeof(string) },
                { "HostName", typeof(string) },
                { "ServerIP", typeof(string) },
                { "TimeTaken", typeof(int) },
                { "BytesSent", typeof(int) },
                { "BytesReceived", typeof(int) },
                { "StatusCode", typeof(int) },
                { "Win32StatusCode", typeof(int) },
                { "RequestType", typeof(string) },
                { "Target", typeof(string) },
                { "Parameters", typeof(string) }
            };

            foreach (XElement e in xml_iis)
            {
                // Required attributes.
                string name;
                string location;


                // Parse required attributes.
                string attributeName;

                attributeName = "name";
                try
                {
                    name = e.Attribute(attributeName).Value;
                }
                catch (NullReferenceException)
                {
                    throw new MissingRequiredAttributeException(e, attributeName);
                }

                attributeName = "location";
                try
                {
                    location = e.Attribute("location").Value;
                }
                catch (NullReferenceException)
                {
                    throw new MissingRequiredAttributeException(e, attributeName);
                }

                // Parse fields.
                IEnumerable<XElement> xml_fields =
                    from el in e.Elements("Fields").Elements("Field")
                    select el;



                List<FieldDefinition> fields = parseFields(xml_fields, allPossibleFields);

                // Parse parameters.
                Params_IISLog args = parseParams_IIS(e.Attributes());


                IISLog iis = new IISLog(name, location, fields, args);
                _iislogs.Add(iis);
            }



            // IISW3C LOGS
            IEnumerable<XElement> xml_iisw3c =
                from el in inputs.Elements("IISW3CLogs").Elements("IISW3CLog")
                select el;

            allPossibleFields = new Dictionary<string, Type>()
                {
                    { "LogFilename", typeof(string) },
                    { "LogRow", typeof(int) },
                    { "date", typeof(DateTime) },
                    { "time", typeof(DateTime) },
                    { "c-ip", typeof(string) },
                    { "cs-username", typeof(string) },
                    { "s-sitename", typeof(string) },
                    { "s-computername", typeof(int) },
                    { "s-ip", typeof(string) },
                    { "s-port", typeof(int) },
                    { "cs-method", typeof(string) },
                    { "cs-uri-stem", typeof(string) },
                    { "cs-uri-query", typeof(string) },
                    { "sc-status", typeof(int) },
                    { "sc-substatus", typeof(int) },
                    { "sc-win32-status", typeof(int) },
                    { "sc-bytes", typeof(int) },
                    { "cs-bytes", typeof(int) },
                    { "time-taken", typeof(int) },
                    { "cs-version", typeof(string) },
                    { "cs-host", typeof(string) },
                    { "cs(User-Agent)", typeof(string) },
                    { "cs(Cookie)", typeof(string) },
                    { "cs(Referer)", typeof(string) },
                    { "s-event", typeof(string) },
                    { "s-process-type", typeof(string) },
                    { "s-user-time", typeof(double) },
                    { "s-kernel-time", typeof(double) },
                    { "s-page-faults", typeof(int) },
                    { "s-total-procs", typeof(int) },
                    { "s-active-procs", typeof(int) },
                    { "s-stopped-procs", typeof(int) }
                };

            foreach (XElement e in xml_iisw3c)
            {
                // Required attributes.
                string name;
                string location;


                // Parse required attributes.
                string attributeName;

                attributeName = "name";
                try
                {
                    name = e.Attribute(attributeName).Value;
                }
                catch (NullReferenceException)
                {
                    throw new MissingRequiredAttributeException(e, attributeName);
                }

                attributeName = "location";
                try
                {
                    location = e.Attribute("location").Value;
                }
                catch (NullReferenceException)
                {
                    throw new MissingRequiredAttributeException(e, attributeName);
                }

                // Parse fields.
                IEnumerable<XElement> xml_fields =
                    from el in e.Elements("Fields").Elements("Field")
                    select el;


                List<FieldDefinition> fields = parseFields(xml_fields, allPossibleFields);

                // Parse parameters.
                Params_IISW3CLog args = parseParams_IISW3C(e.Attributes());


                IISW3CLog iisw3c = new IISW3CLog(name, location, fields, args);
                _iisw3clogs.Add(iisw3c);
            }
        }

        static void parseConfFilter(string xmlConfFile)
        {
            XDocument config = XDocument.Load(xmlConfFile, LoadOptions.SetLineInfo | LoadOptions.SetBaseUri);

            IEnumerable<XElement> filters =
                from el in config.Root.Elements("Filters")
                select el;

            foreach (XElement e in filters.Elements())
            {
                switch (e.Name.ToString())
                {
                    case "Grok":
                        Params_Grok args = parseParams_Grok(e.Elements());
                        GrokFilter grok = new GrokFilter(args);
                        _filters.Add(grok);
                        break;
                    case "Mutate":
                        break;
                }
            }
        }

        static List<FieldDefinition> parseFields(IEnumerable<XElement> xml_fields, Dictionary<string, Type> allPossibleFields)
        {
            List<FieldDefinition> fields = new List<FieldDefinition>();

            foreach (XElement f in xml_fields)
            {
                // Parse field name.
                string name;
                string attributeName = "name";
                try
                {
                    name = f.Attribute(attributeName).Value;
                }
                catch (NullReferenceException)
                {
                    throw new MissingRequiredAttributeException(f, attributeName);
                }

                // Ensure field name is valid.
                if (allPossibleFields.ContainsKey(name))
                {
                    fields.Add(new FieldDefinition(name, allPossibleFields[name]));
                }
                else
                {
                    throw new InvalidAttributeValueException(f.Attribute("name"));
                }
            }

            // If no fields are provided, default to all fields.
            if (fields.Count == 0)
            {
                foreach (KeyValuePair<string, Type> entry in allPossibleFields)
                {
                    fields.Add(new FieldDefinition(entry.Key, entry.Value));
                }
            }

            return fields;
        }

        static Params_WindowsEvent parseParams_Event(IEnumerable<XAttribute> attributes)
        {
            Params_WindowsEvent.Builder p = new Params_WindowsEvent.Builder();

            foreach (XAttribute a in attributes)
            {
                string val = a.Value;
                IXmlLineInfo li = ((IXmlLineInfo)a);

                switch (a.Name.ToString())
                {
                    case "source":
                        break;
                    case "fullText":
                        if (val == "ON" || val == "true")
                        {
                            p.WithFullText(true);
                        }
                        else if (val == "OFF" || val == "false")
                        {
                            p.WithFullText(false);
                        }
                        else
                        {
                            throw new InvalidAttributeValueException(a);
                        }
                        break;
                    case "resolveSIDS":
                        if (val == "ON" || val == "true")
                        {
                            p.WithResolveSIDS(true);
                        }
                        else if (val == "OFF" || val == "false")
                        {
                            p.WithResolveSIDS(false);
                        }
                        else
                        {
                            throw new InvalidAttributeValueException(a);
                        }
                        break;
                    case "formatMsg":
                        if (val == "ON" || val == "true")
                        {
                            p.WithFormatMsg(true);
                        }
                        else if (val == "OFF" || val == "false")
                        {
                            p.WithFormatMsg(false);
                        }
                        else
                        {
                            throw new InvalidAttributeValueException(a);
                        }
                        break;
                    case "msgErrorMode":
                        if (val == "NULL" || val == "ERROR" || val == "MSG")
                        {
                            p.WithMsgErrorMode(val);
                        }
                        else
                        {
                            throw new InvalidAttributeValueException(a);
                        }
                        break;
                    case "fullEventCode":
                        if (val == "ON" || val == "true")
                        {
                            p.WithFullEventCode(true);
                        }
                        else if (val == "OFF" || val == "false")
                        {
                            p.WithFullEventCode(false);
                        }
                        else
                        {
                            throw new InvalidAttributeValueException(a);
                        }
                        break;
                    case "direction":
                        if (val == "FW" || val == "BW")
                        {
                            p.WithDirection(val);
                        }
                        else
                        {
                            throw new InvalidAttributeValueException(a);
                        }
                        break;
                    case "stringsSep":
                        p.WithStringsSep(val);
                        break;
                    case "iCheckpoint":
                        p.WithICheckpoint(val);
                        break;
                    case "binaryFormat":
                        if (val == "ASC" || val == "PRINT" || val == "HEX")
                        {
                            p.WithBinaryFormat(val);
                        }
                        else
                        {
                            throw new InvalidAttributeValueException(a);
                        }
                        break;
                    default:
                        throw new InvalidAttributeNameException(a);
                }
            }

            return p.Build();
        }

        static Params_TextLog parseParams_Log(IEnumerable<XAttribute> attributes)
        {
            Params_TextLog.Builder p = new Params_TextLog.Builder();

            foreach (XAttribute a in attributes)
            {
                string val = a.Value;
                int valInt;

                switch (a.Name.ToString())
                {
                    case "name":
                        break;
                    case "location":
                        break;
                    case "iCodepage":
                        if (int.TryParse(val, out valInt))
                        {
                            p.WithICodepage(valInt);
                        }
                        else
                        {
                            throw new InvalidAttributeIntegerValueException(a);
                        }
                        break;
                    case "recurse":
                        if (int.TryParse(val, out valInt))
                        {
                            p.WithRecurse(valInt);
                        }
                        else
                        {
                            throw new InvalidAttributeIntegerValueException(a);
                        }
                        break;
                    case "splitLongLines":
                        if (val == "ON" || val == "true")
                        {
                            p.WithSplitLongLines(true);
                        }
                        else if (val == "OFF" || val == "false")
                        {
                            p.WithSplitLongLines(false);
                        }
                        else
                        {
                            throw new InvalidAttributeValueException(a);
                        }
                        break;
                    case "iCheckpoint":
                        p.WithICheckpoint(val);
                        break;
                    default:
                        throw new InvalidAttributeNameException(a);
                }
            }

            return p.Build();
        }

        static Params_IISLog parseParams_IIS(IEnumerable<XAttribute> attributes)
        {
            Params_IISLog.Builder p = new Params_IISLog.Builder();

            foreach (XAttribute a in attributes)
            {
                string val = a.Value;
                int valInt;

                switch (a.Name.ToString())
                {
                    case "name":
                        break;
                    case "location":
                        break;
                    case "iCodepage":
                        if (int.TryParse(val, out valInt))
                        {
                            p.WithICodepage(valInt);
                        }
                        else
                        {
                            throw new InvalidAttributeIntegerValueException(a);
                        }
                        break;
                    case "recurse":
                        if (int.TryParse(val, out valInt))
                        {
                            p.WithRecurse(valInt);
                        }
                        else
                        {
                            throw new InvalidAttributeIntegerValueException(a);
                        }
                        break;
                    case "minDateMod":
                        DateTime dt;
                        if (DateTime.TryParseExact(val,
                            "yyyy-MM-dd hh:mm:ss",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out dt))
                        {
                            p.WithMinDateMod(val);
                        }
                        else
                        {
                            throw new InvalidAttributeDateValueException(a);
                        }
                        break;
                    case "locale":
                        p.WithLocale(val);
                        break;
                    case "iCheckpoint":
                        p.WithICheckpoint(val);
                        break;
                    default:
                        throw new InvalidAttributeNameException(a);
                }
            }

            return p.Build();
        }

        static Params_IISW3CLog parseParams_IISW3C(IEnumerable<XAttribute> attributes)
        {
            Params_IISW3CLog.Builder p = new Params_IISW3CLog.Builder();

            foreach (XAttribute a in attributes)
            {
                string val = a.Value;
                int valInt;

                switch (a.Name.ToString())
                {
                    case "name":
                        break;
                    case "location":
                        break;
                    case "iCodepage":
                        if (int.TryParse(val, out valInt))
                        {
                            p.WithICodepage(valInt);
                        }
                        else
                        {
                            throw new InvalidAttributeIntegerValueException(a);
                        }
                        break;
                    case "recurse":
                        if (int.TryParse(val, out valInt))
                        {
                            p.WithRecurse(valInt);
                        }
                        else
                        {
                            throw new InvalidAttributeIntegerValueException(a);
                        }
                        break;
                    case "minDateMod":
                        DateTime dt;
                        if (DateTime.TryParseExact(val,
                            "yyyy-MM-dd hh:mm:ss",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out dt))
                        {
                            p.WithMinDateMod(val);
                        }
                        else
                        {
                            throw new InvalidAttributeDateValueException(a);
                        }
                        break;
                    case "dQuotes":
                        if (val == "ON" || val == "true")
                        {
                            p.WithDQuotes(true);
                        }
                        else if (val == "OFF" || val == "false")
                        {
                            p.WithDQuotes(false);
                        }
                        else
                        {
                            throw new InvalidAttributeValueException(a);
                        }
                        break;
                    case "dirTime":
                        if (val == "ON" || val == "true")
                        {
                            p.WithDirTime(true);
                        }
                        else if (val == "OFF" || val == "false")
                        {
                            p.WithDirTime(false);
                        }
                        else
                        {
                            throw new InvalidAttributeValueException(a);
                        }
                        break;
                    case "consolidateLogs":
                        if (val == "ON" || val == "true")
                        {
                            p.WithConsolidateLogs(true);
                        }
                        else if (val == "OFF" || val == "false")
                        {
                            p.WithConsolidateLogs(false);
                        }
                        else
                        {
                            throw new InvalidAttributeValueException(a);
                        }
                        break;
                    case "iCheckpoint":
                        p.WithICheckpoint(val);
                        break;
                    default:
                        throw new InvalidAttributeNameException(a);
                }
            }

            return p.Build();
        }

        static Params_Grok parseParams_Grok(IEnumerable<XElement> elements)
        {
            Params_Grok.Builder p = new Params_Grok.Builder();

            foreach (XElement e in elements)
            {
                string val;
                string attributeName;
                switch (e.Name.ToString())
                {
                    case "Match":
                        attributeName = "value";
                        try
                        {
                            val = e.Attribute(attributeName).Value;
                        }
                        catch
                        {
                            throw new MissingRequiredAttributeException(e, attributeName);
                        }
                        p.WithMatch(val);

                        attributeName = "field";
                        try
                        {
                            val = e.Attribute(attributeName).Value;
                        }
                        catch
                        {
                            throw new MissingRequiredAttributeException(e, attributeName);
                        }
                        p.WithField(val);
                        break;

                    case "AddField":
                        string name, value;
                        attributeName = "name";
                        try
                        {
                            name = e.Attribute(attributeName).Value;
                        }
                        catch
                        {
                            throw new MissingRequiredAttributeException(e, attributeName);
                        }
                        attributeName = "value";
                        try
                        {
                            value = e.Attribute(attributeName).Value;
                        }
                        catch
                        {
                            throw new MissingRequiredAttributeException(e, attributeName);
                        }
                        Pair addField = new Pair(name, value);
                        p.WithAddField(addField);
                        break;
                    case "DropIfMatch":
                        attributeName = "value";
                        try
                        {
                            val = e.Attribute(attributeName).Value;
                        }
                        catch
                        {
                            throw new MissingRequiredAttributeException(e, attributeName);
                        }
                        if (val == "ON" || val == "true")
                        {
                            p.WithDropIfMatch(true);
                        }
                        else if (val == "OFF" || val == "false")
                        {
                            p.WithDropIfMatch(false);
                        }
                        else
                        {
                            throw new InvalidAttributeValueException(e.Attribute(attributeName));
                        }
                        break;
                    case "RemoveField":
                        attributeName = "value";
                        try
                        {
                            val = e.Attribute(attributeName).Value;
                        }
                        catch
                        {
                            throw new MissingRequiredAttributeException(e, attributeName);
                        }
                        p.WithRemoveField(val);
                        break;
                    default:
                        throw new InvalidElementNameException(e);
                }
            }

            return p.Build();
        }

        public class WindowsEvent
        {
            public string Source { get; private set; }
            public List<FieldDefinition> Fields { get; private set; }

            // Parameters
            public bool FullText { get; private set; }
            public bool ResolveSIDS { get; private set; }
            public bool FormatMsg { get; private set; }
            public string MsgErrorMode { get; private set; }
            public bool FullEventCode { get; private set; }
            public string Direction { get; private set; }
            public string StringsSep { get; private set; }
            public string ICheckpoint { get; private set; }
            public string BinaryFormat { get; private set; }

            public WindowsEvent(string source, List<FieldDefinition> fields, Params_WindowsEvent args)
            {
                Source = source;
                Fields = fields;

                FullText = args.FullText;
                ResolveSIDS = args.ResolveSIDS;
                FormatMsg = args.FormatMsg;
                MsgErrorMode = args.MsgErrorMode;
                Direction = args.Direction;
                StringsSep = args.StringsSep;
                ICheckpoint = args.ICheckpoint;
                BinaryFormat = args.BinaryFormat;
            }

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("WindowsEvent\n");
                sb.Append(String.Format("Source: {0}\n", Source));
                sb.Append("Fields:\n");
                foreach (FieldDefinition f in Fields)
                {
                    sb.Append(String.Format("\t{0}\n", f.Name));
                }
                sb.Append("Parameters:\n");
                sb.Append(String.Format("\tfullText: {0}\n", FullText));
                sb.Append(String.Format("\tresolveSIDS: {0}\n", ResolveSIDS));
                sb.Append(String.Format("\tformatMsg: {0}\n", FormatMsg));
                sb.Append(String.Format("\tmsgErrorMode: {0}\n", MsgErrorMode));
                sb.Append(String.Format("\tfullEventCode: {0}\n", FullEventCode));
                sb.Append(String.Format("\tdirection: {0}\n", Direction));
                sb.Append(String.Format("\tstringsSep: {0}\n", StringsSep));
                sb.Append(String.Format("\tiCheckpoint: {0}\n", ICheckpoint));
                sb.Append(String.Format("\tbinaryFormat: {0}\n", BinaryFormat));

                return sb.ToString();
            }
        }

        public class TailFileInput
        {
            public string Name { get; private set; }
            public string Location { get; private set; }
            public List<FieldDefinition> Fields { get; private set; }

            // Parameters
            public int ICodepage { get; private set; }
            public int Recurse { get; private set; }
            public bool SplitLongLines { get; private set; }
            public string ICheckpoint { get; private set; }

            public TailFileInput(string name, string location, List<FieldDefinition> fields, Params_TextLog args)
            {
                Name = name;
                Location = location;
                Fields = fields;

                ICodepage = args.ICodepage;
                Recurse = args.Recurse;
                SplitLongLines = args.SplitLongLines;
                ICheckpoint = args.ICheckpoint;
            }

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("TextLog\n");
                sb.Append(String.Format("Name: {0}\n", Name));
                sb.Append(String.Format("Location: {0}\n", Location));
                sb.Append("Fields:\n");
                foreach (FieldDefinition f in Fields)
                {
                    sb.Append(String.Format("\t{0}\n", f.Name));
                }
                sb.Append("Parameters:\n");
                sb.Append(String.Format("\tiCodepage: {0}\n", ICodepage));
                sb.Append(String.Format("\trecurse: {0}\n", Recurse));
                sb.Append(String.Format("\tsplitLongLines: {0}\n", SplitLongLines));
                sb.Append(String.Format("\tiCheckpoint: {0}\n", ICheckpoint));

                return sb.ToString();
            }
        }

        public class IISLog
        {
            public string Name { get; private set; }
            public string Location { get; private set; }
            public List<FieldDefinition> Fields { get; private set; }

            // Parameters
            public int ICodepage { get; private set; }
            public int Recurse { get; private set; }
            public string MinDateMod { get; private set; }
            public string Locale { get; private set; }
            public string ICheckpoint { get; private set; }

            public IISLog(string name, string location, List<FieldDefinition> fields, Params_IISLog args)
            {
                Name = name;
                Location = location;
                Fields = fields;

                ICodepage = args.ICodepage;
                Recurse = args.Recurse;
                MinDateMod = args.MinDateMod;
                Locale = args.Locale;
                ICheckpoint = args.ICheckpoint;
            }

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("IISLog\n");
                sb.Append(String.Format("Name: {0}\n", Name));
                sb.Append(String.Format("Location: {0}\n", Location));
                sb.Append("Fields:\n");
                foreach (FieldDefinition f in Fields)
                {
                    sb.Append(String.Format("\t{0}\n", f.Name));
                }
                sb.Append("Parameters:\n");
                sb.Append(String.Format("\tiCodepage: {0}\n", ICodepage));
                sb.Append(String.Format("\trecurse: {0}\n", Recurse));
                sb.Append(String.Format("\tminDateMod: {0}\n", MinDateMod));
                sb.Append(String.Format("\tlocale: {0}\n", Locale));
                sb.Append(String.Format("\tiCheckpoint: {0}\n", ICheckpoint));

                return sb.ToString();
            }
        }

        public class IISW3CLog
        {
            public string Name { get; private set; }
            public string Location { get; private set; }
            public List<FieldDefinition> Fields { get; private set; }

            // Parameters
            public int ICodepage { get; private set; }
            public int Recurse { get; private set; }
            public string MinDateMod { get; private set; }
            public bool DQuotes { get; private set; }
            public bool DirTime { get; private set; }
            public bool ConsolidateLogs { get; private set; }
            public string ICheckpoint { get; private set; }

            public IISW3CLog(string name, string location, List<FieldDefinition> fields, Params_IISW3CLog args)
            {
                Name = name;
                Location = location;
                Fields = fields;

                ICodepage = args.ICodepage;
                Recurse = args.Recurse;
                MinDateMod = args.MinDateMod;
                DQuotes = args.DQuotes;
                DirTime = args.DirTime;
                ConsolidateLogs = args.ConsolidateLogs;
                ICheckpoint = args.ICheckpoint;
            }

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("IISW3CLog\n");
                sb.Append(String.Format("Name: {0}\n", Name));
                sb.Append(String.Format("Location: {0}\n", Location));
                sb.Append("Fields:\n");
                foreach (FieldDefinition f in Fields)
                {
                    sb.Append(String.Format("\t{0}\n", f.Name));
                }
                sb.Append("Parameters:\n");
                sb.Append(String.Format("\tiCodepage: {0}\n", ICodepage));
                sb.Append(String.Format("\trecurse: {0}\n", Recurse));
                sb.Append(String.Format("\tminDateMod: {0}\n", MinDateMod));
                sb.Append(String.Format("\tdQuotes: {0}\n", DQuotes));
                sb.Append(String.Format("\tdirTime: {0}\n", DirTime));
                sb.Append(String.Format("\tconsolidateLogs: {0}\n", ConsolidateLogs));
                sb.Append(String.Format("\tiCheckpoint: {0}\n", ICheckpoint));

                return sb.ToString();
            }
        }

        public class Params_WindowsEvent
        {
            public bool FullText { get; private set; }
            public bool ResolveSIDS { get; private set; }
            public bool FormatMsg { get; private set; }
            public string MsgErrorMode { get; private set; }
            public bool FullEventCode { get; private set; }
            public string Direction { get; private set; }
            public string StringsSep { get; private set; }
            public string ICheckpoint { get; private set; }
            public string BinaryFormat { get; private set; }

            public class Builder
            {
                // Default values for parameters.
                private bool fullText = true;
                private bool resolveSIDS = true;
                private bool formatMsg = true;
                private string msgErrorMode = "MSG";
                private bool fullEventCode = false;
                private string direction = "FW";
                private string stringsSep = "|";
                private string iCheckpoint;
                private string binaryFormat = "PRINT";

                public Builder WithFullText(bool value)
                {
                    fullText = value;
                    return this;
                }

                public Builder WithResolveSIDS(bool value)
                {
                    resolveSIDS = value;
                    return this;
                }

                public Builder WithFormatMsg(bool value)
                {
                    formatMsg = value;
                    return this;
                }

                public Builder WithMsgErrorMode(string value)
                {
                    msgErrorMode = value;
                    return this;
                }

                public Builder WithFullEventCode(bool value)
                {
                    fullEventCode = value;
                    return this;
                }

                public Builder WithDirection(string value)
                {
                    direction = value;
                    return this;
                }

                public Builder WithStringsSep(string value)
                {
                    stringsSep = value;
                    return this;
                }

                public Builder WithICheckpoint(string value)
                {
                    iCheckpoint = value;
                    return this;
                }

                public Builder WithBinaryFormat(string value)
                {
                    binaryFormat = value;
                    return this;
                }

                public Params_WindowsEvent Build()
                {
                    return new Params_WindowsEvent()
                    {
                        FullText = fullText,
                        ResolveSIDS = resolveSIDS,
                        FormatMsg = formatMsg,
                        MsgErrorMode = msgErrorMode,
                        FullEventCode = fullEventCode,
                        Direction = direction,
                        StringsSep = stringsSep,
                        ICheckpoint = iCheckpoint,
                        BinaryFormat = binaryFormat
                    };
                }
            }
        }

        public class Params_TextLog
        {
            public int ICodepage { get; private set; }
            public int Recurse { get; private set; }
            public bool SplitLongLines { get; private set; }
            public string ICheckpoint { get; private set; }

            public class Builder
            {
                // Default values for parameters.
                private int iCodepage = 0;
                private int recurse = 0;
                private bool splitLongLines = false;
                private string iCheckpoint;

                public Builder WithICodepage(int value)
                {
                    iCodepage = value;
                    return this;
                }

                public Builder WithRecurse(int value)
                {
                    recurse = value;
                    return this;
                }

                public Builder WithSplitLongLines(bool value)
                {
                    splitLongLines = value;
                    return this;
                }

                public Builder WithICheckpoint(string value)
                {
                    iCheckpoint = value;
                    return this;
                }

                public Params_TextLog Build()
                {
                    return new Params_TextLog()
                    {
                        ICodepage = iCodepage,
                        Recurse = recurse,
                        SplitLongLines = splitLongLines,
                        ICheckpoint = iCheckpoint
                    };
                }
            }


        }

        public class Params_IISLog
        {
            public int ICodepage { get; private set; }
            public int Recurse { get; private set; }
            public string MinDateMod { get; private set; }
            public string Locale { get; private set; }
            public string ICheckpoint { get; private set; }

            public class Builder
            {
                // Default values for parameters.
                private int iCodepage = -2;
                private int recurse = 0;
                private string minDateMod;
                private string locale = "DEF";
                private string iCheckpoint;

                public Builder WithICodepage(int value)
                {
                    iCodepage = value;
                    return this;
                }

                public Builder WithRecurse(int value)
                {
                    recurse = value;
                    return this;
                }

                public Builder WithMinDateMod(string value)
                {
                    minDateMod = value;
                    return this;
                }

                public Builder WithLocale(string value)
                {
                    locale = value;
                    return this;
                }

                public Builder WithICheckpoint(string value)
                {
                    iCheckpoint = value;
                    return this;
                }

                public Params_IISLog Build()
                {
                    return new Params_IISLog()
                    {
                        ICodepage = iCodepage,
                        Recurse = recurse,
                        MinDateMod = minDateMod,
                        Locale = locale,
                        ICheckpoint = iCheckpoint
                    };
                }
            }
        }

        public class Params_IISW3CLog
        {
            public int ICodepage { get; private set; }
            public int Recurse { get; private set; }
            public string MinDateMod { get; private set; }
            public bool DQuotes { get; private set; }
            public bool DirTime { get; private set; }
            public bool ConsolidateLogs { get; private set; }
            public string ICheckpoint { get; private set; }

            public class Builder
            {
                // Default values for parameters.
                private int iCodepage = -2;
                private int recurse = 0;
                private string minDateMod;
                private bool dQuotes = false;
                private bool dirTime = false;
                private bool consolidateLogs = false;
                private string iCheckpoint;

                public Builder WithICodepage(int value)
                {
                    iCodepage = value;
                    return this;
                }

                public Builder WithRecurse(int value)
                {
                    recurse = value;
                    return this;
                }

                public Builder WithMinDateMod(string value)
                {
                    minDateMod = value;
                    return this;
                }

                public Builder WithDQuotes(bool value)
                {
                    dQuotes = value;
                    return this;
                }

                public Builder WithDirTime(bool value)
                {
                    dirTime = value;
                    return this;
                }

                public Builder WithConsolidateLogs(bool value)
                {
                    consolidateLogs = value;
                    return this;
                }

                public Builder WithICheckpoint(string value)
                {
                    iCheckpoint = value;
                    return this;
                }

                public Params_IISW3CLog Build()
                {
                    return new Params_IISW3CLog()
                    {
                        ICodepage = iCodepage,
                        Recurse = recurse,
                        MinDateMod = minDateMod,
                        DQuotes = dQuotes,
                        DirTime = dirTime,
                        ConsolidateLogs = consolidateLogs,
                        ICheckpoint = iCheckpoint
                    };
                }
            }
        }

        public struct Pair
        {
            public readonly string Name, Value;

            public Pair(string name, string value)
            {
                Name = name;
                Value = value;
            }

            public override string ToString()
            {
                return String.Format("Name:= {0} , Value:= {1}", Name, Value);
            }
        }

    
        public class Params_Grok
        {
            public string Match { get; private set; }
            public string Field { get; private set; }
            public Pair AddField { get; private set; }
            public bool DropIfMatch { get; private set; }
            public string RemoveField { get; private set; }

            public class Builder
            {
                private string match;
                private string field;
                private Pair addField;
                private bool dropIfMatch = false;
                private string removeField;

                public Builder WithField(string value)
                {
                    field = value;
                    return this;
                }

                public Builder WithMatch(string value)
                {
                    match = value;
                    return this;
                }

                public Builder WithAddField(Pair value)
                {
                    addField = value;
                    return this;
                }

                public Builder WithDropIfMatch(bool value)
                {
                    dropIfMatch = value;
                    return this;
                }

                public Builder WithRemoveField(string value)
                {
                    removeField = value;
                    return this;
                }

                public Params_Grok Build()
                {
                    return new Params_Grok()
                    {
                        Match = match,
                        Field = field,
                        AddField = addField,
                        DropIfMatch = dropIfMatch,
                        RemoveField = removeField
                    };
                }

            }
        }

    }
}