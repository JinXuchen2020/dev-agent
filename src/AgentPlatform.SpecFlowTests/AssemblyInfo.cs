using Xunit;

// BDD 集成测试共享同一文件 SQLite（test-integration.db）与单例 WebApplicationFactory。
// xUnit 默认并行执行测试集合，会导致多个 scenario 同时初始化/迁移/清空同一 DB 文件，
// 触发 SQLite 锁与迁移竞争（表现为 14 个 scenario 在 ~260ms 内集体失败）。
// 禁用并行化以保证 scenario 串行执行、DB 状态确定。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
