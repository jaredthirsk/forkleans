using System.Xml.Linq;

namespace TestGrains
{
    /// <summary>
    /// all chat events implement this interface, to define how each event changes the XML document
    /// </summary>
    public interface IChatEvent
    {
        void Update(XDocument document);
    }

    [Serializable]
    [Forkleans.GenerateSerializer]
    public class CreatedEvent : IChatEvent
    {
        [Forkleans.Id(0)]
        public DateTime Timestamp { get; set; }
        [Forkleans.Id(1)]
        public string Origin { get; set; }

        public void Update(XDocument document)
        {
            document.Initialize(Timestamp, Origin);
        }
    }


    [Serializable]
    [Forkleans.GenerateSerializer]
    public class PostedEvent : IChatEvent
    {
        [Forkleans.Id(0)]
        public Guid Guid { get; set; }
        [Forkleans.Id(1)]
        public string User { get; set; }
        [Forkleans.Id(2)]
        public DateTime Timestamp { get; set; }
        [Forkleans.Id(3)]
        public string Text { get; set; }

        public void Update(XDocument document)
        {
            var container = document.GetPostsContainer();
            container.Add(ChatFormat.MakePost(Guid, User, Timestamp, Text));
            document.EnforceLimit();
        }
    }

    [Serializable]
    [Forkleans.GenerateSerializer]
    public class DeletedEvent : IChatEvent
    {
        [Forkleans.Id(0)]
        public Guid Guid { get; set; }

        public void Update(XDocument document)
        {
            document.FindPost(Guid.ToString())?.Remove();
        }
    }

    [Serializable]
    [Forkleans.GenerateSerializer]
    public class EditedEvent : IChatEvent
    {
        [Forkleans.Id(0)]
        public Guid Guid { get; set; }
        [Forkleans.Id(1)]
        public string Text { get; set; }

        public void Update(XDocument document)
        {
            document.FindPost(Guid.ToString())?.ReplaceText(Text);
        }
    }
}
   