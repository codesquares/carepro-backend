using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class GigTemplateCategory
    {
        public ObjectId Id { get; set; }
        public string Name { get; set; }
        public List<string> CategoryTags { get; set; } = new();
        public List<GigTemplateSubcategory> Subcategories { get; set; } = new();
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class GigTemplateSubcategory
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<string> Tags { get; set; } = new();
        public List<GigTemplateSampleTask> SampleTasks { get; set; } = new();
    }

    public class GigTemplateSampleTask
    {
        public string Id { get; set; }
        public string Text { get; set; }
    }
}
