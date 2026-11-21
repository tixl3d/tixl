using System;
using System.IO;
using System.Xml;

namespace libomtnet
{
    internal class OMTRedirect : OMTBase
    {
        private string redirectAddress = null;
        private string redirectAddressUpstream = null;
        private OMTReceive redirectConnection = null;
        private object redirectLock = new object();

        private OMTSend sender = null;
        private string originalAddress = null;
        private OMTReceive receiver = null;

        public OMTRedirect(OMTSend sender)
        {
            this.sender = sender;
            this.originalAddress = sender.Address;
        }

        public OMTRedirect(OMTReceive receiver)
        {
            this.receiver = receiver;
            this.originalAddress = receiver.Address;
        }

        private void ClearRedirectConnection()
        {            
            if (redirectConnection != null)
            {
                redirectAddressUpstream = null;
                redirectConnection.RedirectChanged -= OnRedirectChanged;
                redirectConnection.Dispose();
                redirectConnection = null;
            }
        }
        private OMTMetadata CreateRedirectMetadata()
        {
            string address = this.redirectAddress;
            if (!String.IsNullOrEmpty(this.redirectAddressUpstream))
            {
                address = this.redirectAddressUpstream;
            }
            string xml = OMTRedirect.ToXML(address);
            return new OMTMetadata(0, xml);
        }
        private void SendRedirect()
        {
            if (sender != null)
            {
                OMTMetadata metadata = CreateRedirectMetadata();
                sender.SendMetadata(metadata, null);
            }
        }

        public void OnNewConnection(OMTChannel ch)
        {
            OMTMetadata m = CreateRedirectMetadata();
            int result = ch.Send(m);
        }

        public void OnReceiveChanged()
        {
            lock (redirectLock)
            {
                if (Exiting) return;
                string newAddress = receiver.RedirectAddress;
                this.redirectAddress = newAddress;
                CreateRedirectConnection(newAddress, originalAddress);             
            }
        }

        public void CheckConnection()
        {
            if (redirectConnection != null)
            {
                redirectConnection.CheckConnection();
            }
        }

        private void CreateRedirectConnection(string newAddress, string sideChannelAddress)
        {
            if (String.IsNullOrEmpty(newAddress))
            {
                OMTLogging.Write("Redirect stopped for " + originalAddress, "OMTRedirect");
                ClearRedirectConnection();
            }
            else
            {
                OMTLogging.Write("Redirecting " + originalAddress + " to " + newAddress + " and monitoring for updates from " + sideChannelAddress, "OMTRedirect");
                if (redirectConnection != null)
                {
                    if (redirectConnection.Address != sideChannelAddress)
                    {
                        ClearRedirectConnection();
                    }
                }
                if (redirectConnection == null)
                {
                    redirectConnection = new OMTReceive(sideChannelAddress, OMTFrameType.Metadata, OMTPreferredVideoFormat.UYVY, OMTReceiveFlags.None);
                    redirectConnection.redirectMetadataOnly = true;
                    redirectConnection.RedirectChanged += OnRedirectChanged;
                }
            }
        }

        public void SetRedirect(string newAddress)
        {
            lock (redirectLock)
            {
                if (Exiting) return;
                if (this.originalAddress == newAddress)
                {
                    newAddress = null; //No redirect in case of loopback
                }
                if (this.redirectAddress != newAddress)
                {
                    this.redirectAddressUpstream = null;
                }
                this.redirectAddress = newAddress;
                SendRedirect();
                CreateRedirectConnection(newAddress, newAddress);
            }
        }
        private void OnRedirectChanged(object sender, OMTRedirectChangedEventArgs e)
        {
            try
            {
                //This is called by the Receive_Completed on the channel own by this object's receiver, 
                //so care needs to be taken to ensure this does not go back into that receiver where that lock may be used.
                if (Exiting) return;
                if (redirectConnection != null)
                {
                    string newAddress = e.NewAddress;
                    if (newAddress != originalAddress)
                    {
                        if (newAddress != redirectAddress)
                        {
                            if (this.sender != null)
                            {
                                if (newAddress != redirectAddressUpstream)
                                {
                                    this.redirectAddressUpstream = newAddress;
                                    OMTLogging.Write("Redirect changed upstream for " + originalAddress + " to " + newAddress, "OMTRedirect");
                                    SendRedirect();
                                }
                            } else if (this.receiver != null)
                            {
                                this.redirectAddress = newAddress;
                                receiver.OnRedirectConnection(newAddress);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OMTLogging.Write(ex.ToString(), "OMTRedirect");
            }
        }

        public static string FromXML(string xml)
        {
            XmlDocument doc = OMTMetadataUtils.TryParse(xml);
            if (doc != null)
            {
                if (doc.DocumentElement != null)
                {
                    XmlNode a = doc.DocumentElement.Attributes.GetNamedItem("NewAddress");
                    if (a != null)
                    {
                        return a.InnerText;
                    }
                }
            }
            return null;
        }
        public static string ToXML(string address)
        {
            using (StringWriter sw = new StringWriter())
            {
                using (XmlTextWriter t = new XmlTextWriter(sw))
                {
                    t.Formatting = Formatting.Indented;
                    t.WriteStartElement(OMTMetadataTemplates.REDIRECT_NAME);
                    t.WriteAttributeString("NewAddress", address);
                    t.WriteEndElement();
                    return sw.ToString();
                }
            }
        }

        protected override void DisposeInternal()
        {
            lock (redirectLock) { }
            sender = null;
            ClearRedirectConnection();
            base.DisposeInternal();
        }
    }
}
