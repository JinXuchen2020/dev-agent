namespace AgentPlatform.SpecFlowTests;

/// <summary>
/// 集成测试共享常量。前后端 E2E 与后端 BDD 共用同一组租户 / 密钥 / 工作流标识，
/// 保证种子数据一致，避免漂移（设计文档 §11 风险 4）。
/// </summary>
public static class IntegrationConstants
{
    /// <summary>默认租户（T1）——DatabaseInitializer 的种子 admin 用户落在此租户。</summary>
    public static readonly Guid Tenant1Id = Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>第二租户（T2）——用于跨租户隔离负向场景。</summary>
    public static readonly Guid Tenant2Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>T1 种子 admin 用户（由 DatabaseInitializer 在 Integration 环境创建）。</summary>
    public const string AdminEmail = "admin@acme.io";

    public const string AdminPassword = "Admin@123456";

    /// <summary>T2 种子用户（由 IntegrationSeeder 创建）。</summary>
    public const string Tenant2UserEmail = "integration-tenant2@test.io";

    public const string Tenant2UserPassword = "Integration@123456";

    /// <summary>T1 非 Admin 用户（role=development，测试 RBAC 403 用）。</summary>
    public const string NonAdminEmail = "integration-member@acme.io";

    public const string NonAdminPassword = "Member@123456";

    /// <summary>T1 绑定密钥明文（种子落库经加密服务）。</summary>
    public const string T1ApiKeyPlaintext = "test-integration-key-t1";

    /// <summary>T2 绑定密钥明文。</summary>
    public const string T2ApiKeyPlaintext = "test-integration-key-t2";

    /// <summary>F29 demo 自主 agent（DatabaseInitializer 播种，默认租户 T1）——F36 agent 会话归属。</summary>
    public static readonly Guid F29DemoAgentId = Guid.Parse("33333333-3333-3333-3333-333333333301");

    /// <summary>F36 agent 归属会话种子（归属 F29DemoAgentId，挂 SampleWorkflowId）。</summary>
    public static readonly Guid AgentConversationId = Guid.Parse("55555555-5555-5555-5555-555555555501");

    /// <summary>固定密钥 Id，便于 Steps 在发布时引用（绑定 Key 场景）。</summary>
    public static readonly Guid T1ApiKeyId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly Guid T2ApiKeyId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>T1 示例 Completed 工作流（F22 发布/运行场景）。</summary>
    public static readonly Guid SampleWorkflowId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>T2 示例 Completed 工作流（跨租户负向场景，需先经 T2 发布）。</summary>
    public static readonly Guid SampleWorkflow2Id = Guid.Parse("55555555-5555-5555-5555-555555555555");

    /// <summary>T1 第二条 Completed 工作流（MCP tools/list 过滤负向：发布为 Api 模式，应被排除）。</summary>
    public static readonly Guid SampleWorkflow3Id = Guid.Parse("66666666-6666-6666-6666-666666666666");
}
