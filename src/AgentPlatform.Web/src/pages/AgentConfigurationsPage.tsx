import React, { useEffect, useState, useCallback } from 'react';
import {
  Table,
  Typography,
  Tag,
  Spin,
  Drawer,
  Descriptions,
  Button,
  Tabs,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { AgentConfiguration } from '../types';
import { getAgentConfigurations } from '../services/api';
import CredentialManager from '../components/CredentialManager';
import { CredentialCategory } from '../types';

const columns = (onView: (r: AgentConfiguration) => void): ColumnsType<AgentConfiguration> => [
  { title: 'Name', dataIndex: 'name', key: 'name' },
  { title: 'Type', dataIndex: 'agentType', key: 'agentType' },
  { title: 'Version', dataIndex: 'version', key: 'version' },
  {
    title: 'Active',
    dataIndex: 'isActive',
    key: 'isActive',
    render: (a: boolean) => (a ? <Tag color="green">Active</Tag> : <Tag>Inactive</Tag>),
  },
  {
    title: 'Created',
    dataIndex: 'createdAt',
    key: 'createdAt',
    render: (d: string) => new Date(d).toLocaleString(),
  },
  {
    title: 'Action',
    key: 'action',
    render: (_, r) => (
      <Button
        size="small"
        onClick={(e) => {
          e.stopPropagation();
          onView(r);
        }}
      >
        View
      </Button>
    ),
  },
];

const AgentConfigurationsPage: React.FC = () => {
  const [configs, setConfigs] = useState<AgentConfiguration[]>([]);
  const [loading, setLoading] = useState(true);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [selected, setSelected] = useState<AgentConfiguration | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);

  const fetch = useCallback((p: number, ps: number, signal?: AbortSignal) => {
    setLoading(true);
    getAgentConfigurations({ skip: (p - 1) * ps, take: ps, signal })
      .then((d) => {
        setConfigs(d.items);
        setTotal(d.totalCount);
      })
      .catch((err: unknown) => {
        // AbortController 取消的请求忽略；其余错误已由全局拦截器记录
        if ((err as { name?: string })?.name !== 'CanceledError')
          console.error('[AgentConfigurations] fetch failed', err);
      })
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    fetch(page, pageSize, controller.signal);
    return () => controller.abort();
  }, [fetch, page, pageSize]);

  const openDrawer = (r: AgentConfiguration) => {
    setSelected(r);
    setDrawerOpen(true);
  };

  const configsTab = loading ? (
    <Spin />
  ) : (
    <Table
      columns={columns(openDrawer)}
      dataSource={configs}
      rowKey="id"
      pagination={{
        current: page,
        pageSize,
        total,
        showTotal: (t) => `共 ${t} 条`,
      }}
      onChange={(p) => {
        setPage(p.current ?? 1);
        setPageSize(p.pageSize ?? 10);
      }}
    />
  );

  const tabItems = [
    { key: 'configs', label: 'Agent 配置', children: configsTab },
    {
      key: 'creds',
      label: '凭据设置',
      children: (
        <Tabs
          defaultActiveKey="model"
          items={[
            {
              key: 'model',
              label: '模型',
              children: <CredentialManager category={CredentialCategory.Model} />,
            },
            {
              key: 'search',
              label: '搜索',
              children: <CredentialManager category={CredentialCategory.Search} />,
            },
          ]}
        />
      ),
    },
  ];

  return (
    <div>
      <Typography.Title level={4}>Agent Configurations</Typography.Title>
      <Tabs defaultActiveKey="configs" items={tabItems} />

      <Drawer
        title="Agent Configuration"
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        width={640}
      >
        {selected && (
          <>
            <Descriptions column={1} bordered size="small" style={{ marginBottom: 16 }}>
              <Descriptions.Item label="Name">{selected.name}</Descriptions.Item>
              <Descriptions.Item label="Type">{selected.agentType}</Descriptions.Item>
              <Descriptions.Item label="Version">{selected.version}</Descriptions.Item>
              <Descriptions.Item label="Active">{selected.isActive ? 'Yes' : 'No'}</Descriptions.Item>
              <Descriptions.Item label="Created">
                {new Date(selected.createdAt).toLocaleString()}
              </Descriptions.Item>
            </Descriptions>
            <pre
              style={{
                background: '#0d1117',
                color: '#e6edf3',
                padding: 16,
                borderRadius: 8,
                overflow: 'auto',
                maxHeight: 420,
                fontSize: 12,
                lineHeight: 1.5,
                whiteSpace: 'pre-wrap',
                wordBreak: 'break-word',
                margin: 0,
              }}
            >
              {selected.yamlContent}
            </pre>
          </>
        )}
      </Drawer>
    </div>
  );
};

export default AgentConfigurationsPage;
