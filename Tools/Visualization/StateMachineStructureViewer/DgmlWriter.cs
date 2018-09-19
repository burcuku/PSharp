﻿using Microsoft.PSharp.LanguageServices;
using Microsoft.PSharp.LanguageServices.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Microsoft.PSharp.PSharpStateMachineStructureViewer
{
    class DgmlWriter
    {
        private readonly static char[] unfriendlyNameSeparators = { '.' };
        private static string FriendlyName(string uniqueName)
        {
            return uniqueName.Split(unfriendlyNameSeparators).Last();
        }

        private static string InheritedName(string uniqueName, string parentName)
        {
            return parentName + ">" + uniqueName;
        }
        /*
        private static bool IsStateInherited(MachineInfo machine, StateInfo state)
        {
            return state.uniqueName.StartsWith(machine.uniqueName);
        }*/

        public static void WriteAll(IEnumerable<MachineInfo> machines, XmlTextWriter writer)
        {

            writer.WriteStartElement("DirectedGraph", @"http://schemas.microsoft.com/vs/2009/dgml");

            writer.WriteStartElement("Nodes");
            writer.WriteComment(" Start Machines ");
            bool drawMachinesExpanded = (machines.ToList().Count <= 1);
            foreach (MachineInfo mInfo in machines)
            {
                WriteMachine(mInfo, writer, drawMachinesExpanded);
            }
            writer.WriteComment(" End Machines ");
            // Move on to the states within the machines
            writer.WriteComment(" Start States");
            foreach (MachineInfo mInfo in machines)
            {
                WriteMachineStates(mInfo, writer);
            }
            writer.WriteComment(" End States ");
            writer.WriteEndElement(/*"Nodes"*/);

            // On to the edges

            writer.WriteStartElement("Links");
            foreach (MachineInfo mInfo in machines)
            {
                WriteMachineStateLinks(mInfo, writer);
            }

            foreach (MachineInfo mInfo in machines)
            {
                foreach(string stateName in mInfo.GetStates())
                {
                    WriteStateTransitions(ResolutionHelper.Instance().GetState(stateName), writer, mInfo);
                }
            }
            writer.WriteEndElement(/*"Links"*/);

            WriteAppendix(writer);

            writer.WriteEndElement(/*DirectedGraph*/);
        }

        private static void WriteAppendix(XmlTextWriter writer)
        {
            // Properties : Define custom properties to show Ignored, Deferred and Handled events
            writer.WriteStartElement("Properties");
            
            string[] customProperties = {
                "Ignores", "Defers", "Handles", /* Vertex properties */
                "Event" /* Edge properties */
            };
            foreach (string propertyName in customProperties)
            {
                writer.WriteStartElement("Property");
                writer.WriteAttributeString("Id", propertyName);
                writer.WriteAttributeString("DataType", "System.String");
                writer.WriteEndElement();
            }
            
            writer.WriteEndElement(/*"Properties"*/);
            

            // Categories
            writer.WriteStartElement("Categories");

            writer.WriteStartElement("Category");
            writer.WriteAttributeString("Id", "GotoTransition");
            writer.WriteEndElement();
            writer.WriteStartElement("Category");
            writer.WriteAttributeString("Id", "PushTransition");
            writer.WriteAttributeString("StrokeDashArray", "2");
            writer.WriteEndElement(/*"Category"*/);

            writer.WriteEndElement(/*"Categories"*/);

            // Some styling?
            Tuple<string, string>[] booleanBackgroundStyles = new Tuple<string,string>[]{
                new Tuple<string,string>("Inherited", "#88888888"),
                new Tuple<string,string>("IsStart", "#bbbbffbb")
            };

            writer.WriteStartElement("Styles");
            foreach (var bbs in booleanBackgroundStyles) {
                writer.WriteStartElement("Style");
                writer.WriteAttributeString("TargetType", "Node");
                writer.WriteStartElement("Condition");
                writer.WriteAttributeString("Expression", bbs.Item1);
                writer.WriteEndElement(/*"Condition"*/);
                writer.WriteStartElement("Setter");
                writer.WriteAttributeString("Property", "Background");
                writer.WriteAttributeString("Value", bbs.Item2);
                writer.WriteEndElement(/*"Setter"*/);
                writer.WriteEndElement(/*"Style"*/);
            }
            writer.WriteEndElement(/*"Styles"*/);
        }

        public static void WriteMachine(MachineInfo machineInfo, XmlTextWriter writer, bool drawExpanded=false)
        {
            writer.WriteStartElement("Node");
            writer.WriteAttributeString("Id", machineInfo.uniqueName);
            writer.WriteAttributeString("Category", "Machine");
            writer.WriteAttributeString("Label", FriendlyName(machineInfo.uniqueName));
            writer.WriteAttributeString("Group", (drawExpanded?"Expanded":"Collapsed"));
            writer.WriteEndElement(/*"Node"*/);
        }
        public static void WriteMachineStateLinks(MachineInfo mInfo, XmlTextWriter writer)
        {
            var machine = mInfo.uniqueName;
            foreach (string stateName in mInfo.GetStates() )
            {
                string stateId = IsInherited(stateName, mInfo.uniqueName) ? 
                    InheritedName(stateName, mInfo.uniqueName) : stateName;
                writer.WriteStartElement("Link");
                writer.WriteAttributeString("Source", machine);
                writer.WriteAttributeString("Target", stateId);
                writer.WriteAttributeString("Category", "Contains");
                writer.WriteEndElement(/*"Link"*/);
            }
        }


        public static void WriteMachineStates(MachineInfo machineInfo, XmlTextWriter writer)
        {
            var machine = machineInfo.uniqueName;
            writer.WriteComment(String.Format("Start states for Machine '{0}'", machineInfo.uniqueName));
            foreach (string stateName in machineInfo.GetStates())
            {
                StateInfo stateInfo = ResolutionHelper.Instance().GetState( stateName );
                bool isInherited = IsInherited( stateInfo.uniqueName, machineInfo.uniqueName);
                string nodeId = isInherited ? 
                    InheritedName(stateInfo.uniqueName, machineInfo.uniqueName) : stateInfo.uniqueName;

                
                writer.WriteStartElement("Node");
                writer.WriteAttributeString("Id", nodeId);
                writer.WriteAttributeString("Category", "State");
                writer.WriteAttributeString("Label", FriendlyName(stateName) );

                if (stateInfo.isStartState && !isInherited)
                {
                    writer.WriteAttributeString("IsStart", "true");
                }
                if( isInherited)
                {
                    writer.WriteAttributeString("Inherited", "true");
                }
                if ( /*TODO*/ true)
                {
                    writer.WriteAttributeString("Ignores", string.Join(", ", stateInfo.GetIgnoredEvents(true)));
                    writer.WriteAttributeString("Defers", string.Join(", ", stateInfo.GetDeferredEvents(true)));
                    writer.WriteAttributeString("Handles", string.Join(", ", stateInfo.GetHandledEvents(true)));
                }

                writer.WriteEndElement();
            }
            writer.WriteComment(String.Format("End states for Machine '{0}'", machineInfo.uniqueName));
        }

        private static bool IsInherited(string propertyName, string ownerName)
        {
            return !propertyName.StartsWith(ownerName);
        }

        public static void WriteStateTransitions(StateInfo sInfo, XmlTextWriter writer, MachineInfo machineContext) {

            string sourceId = IsInherited(sInfo.uniqueName, machineContext.uniqueName) ?
                InheritedName(sInfo.uniqueName, machineContext.uniqueName) : sInfo.uniqueName;
            writer.WriteComment(String.Format("Start outgoing transitions from {0}", sourceId));

            foreach (var kvp in sInfo.GetGotoTransitions())
            {
                string targetId = IsInherited(kvp.Value, machineContext.uniqueName)?
                    InheritedName(kvp.Value, machineContext.uniqueName) : kvp.Value ;
                writer.WriteStartElement("Link");
                writer.WriteAttributeString("Source", sourceId);
                writer.WriteAttributeString("Target", targetId);
                writer.WriteAttributeString("Event", kvp.Key);
                writer.WriteAttributeString("Category", "GotoTransition");
                writer.WriteAttributeString("Label", FriendlyName(kvp.Key) );
                writer.WriteEndElement();
            }

            foreach (var kvp in sInfo.GetPushTransitions())
            {
                writer.WriteStartElement("Link");
                writer.WriteAttributeString("Source", sourceId);
                writer.WriteAttributeString("Target", kvp.Value);
                writer.WriteAttributeString("Category", "PushTransition");
                writer.WriteAttributeString("Label", FriendlyName(kvp.Key));
                writer.WriteEndElement();
            }
            writer.WriteComment(String.Format("End outgoing transitions from {0}", sourceId));
        }
        

        


    }
}
