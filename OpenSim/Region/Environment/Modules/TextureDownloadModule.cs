using System;
using System.Collections.Generic;
using System.Text;
using libsecondlife;
using OpenSim.Framework.Interfaces;
using OpenSim.Region.Environment.Scenes;
using OpenSim.Region.Environment.Interfaces;

namespace OpenSim.Region.Environment.Modules
{
    public class TextureDownloadModule :IRegionModule
    {
        private Scene m_scene;

        public TextureDownloadModule()
        {

        }

        public void Initialise(Scene scene)
        {
            m_scene = scene;
            m_scene.EventManager.OnNewClient += NewClient;
        }

        public void PostInitialise()
        {

        }

        public void CloseDown()
        {
        }

        public string GetName()
        {
            return "TextureDownloadModule";
        }

        public void NewClient(IClientAPI client)
        {
        }

        public void TextureAssetCallback(LLUUID texture, byte[] data)
        {

        }
    }
}
