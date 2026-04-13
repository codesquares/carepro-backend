namespace Application.DTOs
{
    public class GigTemplateResponseDTO
    {
        public List<GigTemplateCategoryDTO> Categories { get; set; } = new();
    }

    public class GigTemplateCategoryDTO
    {
        public string Name { get; set; }
        public List<string> CategoryTags { get; set; } = new();
        public List<GigTemplateSubcategoryDTO> Subcategories { get; set; } = new();
    }

    public class GigTemplateSubcategoryDTO
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<string> Tags { get; set; } = new();
        public List<GigTemplateSampleTaskDTO> SampleTasks { get; set; } = new();
    }

    public class GigTemplateSampleTaskDTO
    {
        public string Id { get; set; }
        public string Text { get; set; }
    }
}
