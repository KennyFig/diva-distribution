/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Net;
using System.Threading;
using OpenMetaverse;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using OpenSim.Framework;
using OpenSim.Framework.Client;
using OpenSim.Region.Interfaces;
using OpenSim.Region.Environment.Interfaces;
using OpenSim.Region.Environment.Scenes;

namespace OpenSim.Region.Environment.Modules.Avatar.InstantMessage
{
    public class PresenceModule : IRegionModule, IPresenceModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private bool m_Enabled = false;
        private bool m_Gridmode = false;
        private List<Scene> m_Scenes = new List<Scene>();

        private Dictionary<UUID, Scene> m_RootAgents =
                new Dictionary<UUID, Scene>();

        public event PresenceChange OnPresenceChange;
        public event BulkPresenceData OnBulkPresenceData;

        public void Initialise(Scene scene, IConfigSource config)
        {
            IConfig cnf = config.Configs["Messaging"];
            if (cnf != null && cnf.GetString(
                    "PresenceModule", "PresenceModule") !=
                    "PresenceModule")
                return;

            cnf = config.Configs["Startup"];
            if (cnf != null)
                m_Gridmode = cnf.GetBoolean("gridmode", false);

            m_Enabled = true;

            lock (m_Scenes)
            {
                if (m_Gridmode)
                    NotifyMessageServerOfStartup(scene);

                scene.RegisterModuleInterface<IPresenceModule>(this);

                scene.EventManager.OnNewClient += OnNewClient;
                scene.EventManager.OnSetRootAgentScene += OnSetRootAgentScene;

                m_Scenes.Add(scene);
            }
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
            if (!m_Gridmode || !m_Enabled)
                return;

            foreach (Scene scene in m_Scenes)
                NotifyMessageServerOfShutdown(scene);
        }

        public string Name
        {
            get { return "PresenceModule"; }
        }

        public bool IsSharedModule
        {
            get { return true; }
        }

        public void RequestBulkPresenceData(UUID[] users)
        {
        }

        public void OnNewClient(IClientAPI client)
        {
            client.OnConnectionClosed += OnConnectionClosed;
            client.OnLogout += OnConnectionClosed;
        }

        public void OnConnectionClosed(IClientAPI client)
        {
            if (!(client.Scene is Scene))
                return;

            if (!(m_RootAgents.ContainsKey(client.AgentId)))
                return;

            Scene scene = (Scene)client.Scene;

            if (m_RootAgents[client.AgentId] != scene)
                return;

            m_RootAgents.Remove(client.AgentId);

            NotifyMessageServerOfAgentLeaving(client.AgentId, scene.RegionInfo.RegionID, scene.RegionInfo.RegionHandle);
        }

        public void OnSetRootAgentScene(UUID agentID, Scene scene)
        {
            if (m_RootAgents.ContainsKey(agentID))
            {
                if (m_RootAgents[agentID] == scene)
                    return;
            }
            m_RootAgents[agentID] = scene;
            NotifyMessageServerOfAgentLocation(agentID, scene.RegionInfo.RegionID, scene.RegionInfo.RegionHandle);
        }

        private void NotifyMessageServerOfStartup(Scene scene)
        {
            Hashtable xmlrpcdata = new Hashtable();
            xmlrpcdata["RegionUUID"] = scene.RegionInfo.RegionID.ToString();
            ArrayList SendParams = new ArrayList();
            SendParams.Add(xmlrpcdata);
            try
            {
                XmlRpcRequest UpRequest = new XmlRpcRequest("region_startup", SendParams);
                XmlRpcResponse resp = UpRequest.Send(scene.CommsManager.NetworkServersInfo.MessagingURL, 5000);

                Hashtable responseData = (Hashtable)resp.Value;
                if (responseData == null || (!responseData.ContainsKey("success")) || (string)responseData["success"] != "TRUE")
                {
                    m_log.ErrorFormat("[PRESENCE] Failed to notify message server of region startup for region {0}", scene.RegionInfo.RegionName);
                }
            }
            catch (System.Net.WebException)
            {
                m_log.ErrorFormat("[PRESENCE] Failed to notify message server of region startup for region {0}", scene.RegionInfo.RegionName);
            }
        }

        private void NotifyMessageServerOfShutdown(Scene scene)
        {
            Hashtable xmlrpcdata = new Hashtable();
            xmlrpcdata["RegionUUID"] = scene.RegionInfo.RegionID.ToString();
            ArrayList SendParams = new ArrayList();
            SendParams.Add(xmlrpcdata);
            try
            {
                XmlRpcRequest DownRequest = new XmlRpcRequest("region_shutdown", SendParams);
                XmlRpcResponse resp = DownRequest.Send(scene.CommsManager.NetworkServersInfo.MessagingURL, 5000);

                Hashtable responseData = (Hashtable)resp.Value;
                if ((!responseData.ContainsKey("success")) || (string)responseData["success"] != "TRUE")
                {
                    m_log.ErrorFormat("[PRESENCE] Failed to notify message server of region shutdown for region {0}", scene.RegionInfo.RegionName);
                }
            }
            catch (System.Net.WebException)
            {
                m_log.ErrorFormat("[PRESENCE] Failed to notify message server of region shutdown for region {0}", scene.RegionInfo.RegionName);
            }
        }

        private void NotifyMessageServerOfAgentLocation(UUID agentID, UUID region, ulong regionHandle)
        {
            Hashtable xmlrpcdata = new Hashtable();
            xmlrpcdata["AgentID"] = agentID.ToString();
            xmlrpcdata["RegionUUID"] = region.ToString();
            xmlrpcdata["RegionHandle"] = regionHandle.ToString();
            ArrayList SendParams = new ArrayList();
            SendParams.Add(xmlrpcdata);
            try
            {
                XmlRpcRequest LocationRequest = new XmlRpcRequest("agent_location", SendParams);
                XmlRpcResponse resp = LocationRequest.Send(m_Scenes[0].CommsManager.NetworkServersInfo.MessagingURL, 5000);

                Hashtable responseData = (Hashtable)resp.Value;
                if ((!responseData.ContainsKey("success")) || (string)responseData["success"] != "TRUE")
                {
                    m_log.ErrorFormat("[PRESENCE] Failed to notify message server of agent location for {0}", agentID.ToString());
                }
            }
            catch (System.Net.WebException)
            {
                m_log.ErrorFormat("[PRESENCE] Failed to notify message server of agent location for {0}", agentID.ToString());
            }
        }

        private void NotifyMessageServerOfAgentLeaving(UUID agentID, UUID region, ulong regionHandle)
        {
            Hashtable xmlrpcdata = new Hashtable();
            xmlrpcdata["AgentID"] = agentID.ToString();
            xmlrpcdata["RegionUUID"] = region.ToString();
            xmlrpcdata["RegionHandle"] = regionHandle.ToString();
            ArrayList SendParams = new ArrayList();
            SendParams.Add(xmlrpcdata);
            try
            {
                XmlRpcRequest LeavingRequest = new XmlRpcRequest("agent_leaving", SendParams);
                XmlRpcResponse resp = LeavingRequest.Send(m_Scenes[0].CommsManager.NetworkServersInfo.MessagingURL, 5000);

                Hashtable responseData = (Hashtable)resp.Value;
                if ((!responseData.ContainsKey("success")) || (string)responseData["success"] != "TRUE")
                {
                    m_log.ErrorFormat("[PRESENCE] Failed to notify message server of agent leaving for {0}", agentID.ToString());
                }
            }
            catch (System.Net.WebException)
            {
                m_log.ErrorFormat("[PRESENCE] Failed to notify message server of agent leaving for {0}", agentID.ToString());
            }
        }
    }
}