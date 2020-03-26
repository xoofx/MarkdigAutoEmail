using Markdig;
using Xunit;

namespace MarkdigAutoEmail.Test
{
    public class AutoEmailLinkingExtensionTest
    {
        [Theory]
        [InlineData("someone@example.com", @"<p><a href=""mailto:someone@example.com"">someone@example.com</a></p>")]
        [InlineData("<someone@example.com>", @"<p><a href=""mailto:someone@example.com"">someone@example.com</a></p>")]
        [InlineData("mailto:someone@example.com", @"<p><a href=""mailto:someone@example.com"">someone@example.com</a></p>")]
        [InlineData("Ask *someone.else@example.com*", @"<p>Ask <em><a href=""mailto:someone.else@example.com"">someone.else@example.com</a></em></p>")]
        [InlineData("Please ask **someone@example.com**", @"<p>Please ask <strong><a href=""mailto:someone@example.com"">someone@example.com</a></strong></p>")]
        [InlineData("someone@example.com is the person to get in touch with", @"<p><a href=""mailto:someone@example.com"">someone@example.com</a> is the person to get in touch with</p>")]
        [InlineData(@"Leave <a href=""mailto:foo@example.com"">foo@example.com</a> existing  links alone", @"<p>Leave <a href=""mailto:foo@example.com"">foo@example.com</a> existing  links alone</p>")]
        [InlineData("Send an email to someonee@example.com", @"<p>Send an email to <a href=""mailto:someone@example.com"">someone.else@example.com</a></p>")]
        public void ShouldAutoLinkEmailOnStart(string markdown, string expectedHtml)
        {
            var markdownPipeline = new MarkdownPipelineBuilder()
                .UseAutoLinks()
                .UseAutoEmailLinks()
                .Build();

            var html = Markdown.ToHtml(markdown, markdownPipeline);

            Assert.Equal(expectedHtml, html.Trim(), ignoreLineEndingDifferences: true);
        }
    }
}