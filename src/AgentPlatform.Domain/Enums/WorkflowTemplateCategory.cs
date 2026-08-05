namespace AgentPlatform.Domain.Enums;

/// <summary>
/// 模板市场分类（F23）。硬编码枚举（决策 S4）——v1 仅平台内置种子，UGC 分类后续 feature。
/// </summary>
public enum WorkflowTemplateCategory
{
    /// <summary>通用 / 未分类。</summary>
    General = 0,

    /// <summary>知识库问答。</summary>
    KnowledgeQa = 1,

    /// <summary>文档摘要。</summary>
    Summarization = 2,

    /// <summary>定时 / 网页抓取。</summary>
    WebScraping = 3,

    /// <summary>多 Agent 评审。</summary>
    MultiAgentReview = 4,

    /// <summary>客服分流。</summary>
    CustomerSupport = 5,

    /// <summary>内容生成。</summary>
    ContentGeneration = 6,

    /// <summary>数据分析。</summary>
    DataAnalysis = 7
}
