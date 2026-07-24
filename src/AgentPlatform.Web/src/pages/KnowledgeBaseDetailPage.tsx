import React, { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Table, Button, Spin, Space, Tag, Upload, Descriptions, App as AntApp } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { ArrowLeftOutlined, UploadOutlined } from '@ant-design/icons';
import type { KnowledgeDocument } from '../types';
import { getKnowledgeBase, uploadDocument } from '../services/api';
import PageHeader from '../components/PageHeader';
import Card from '../components/Card';
import { colors } from '../theme/tokens';

const KnowledgeBaseDetailPage: React.FC = () => {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const [kb, setKb] = useState<{ name: string; collectionName: string; embeddingModel: string; description: string } | null>(null);
  const [documents, setDocuments] = useState<KnowledgeDocument[]>([]);
  const [loading, setLoading] = useState(true);
  const [uploading, setUploading] = useState(false);
  const { message } = AntApp.useApp();

  const load = () => {
    setLoading(true);
    getKnowledgeBase(id)
      .then((data) => {
        setKb({ name: data.name, collectionName: data.collectionName, embeddingModel: data.embeddingModel, description: data.description });
        setDocuments(data.documents);
      })
      .catch(() => message.error('加载知识库详情失败'))
      .finally(() => setLoading(false));
  };

  useEffect(load, [id]);

  const handleUpload = async (file: File) => {
    setUploading(true);
    try {
      await uploadDocument(id, file);
      message.success(`文档「${file.name}」已切分入库`);
      load();
    } catch {
      message.error('上传失败');
    } finally {
      setUploading(false);
    }
    return false; // 阻止 antd 自动上传
  };

  const columns: ColumnsType<KnowledgeDocument> = [
    {
      title: '文件名',
      dataIndex: 'fileName',
      key: 'fileName',
      render: (n: string) => <span style={{ color: colors.textPrimary, fontWeight: 500 }}>{n}</span>,
    },
    {
      title: '类型',
      dataIndex: 'contentType',
      key: 'contentType',
      width: 160,
      render: (t: string) => <Tag>{t || 'text/plain'}</Tag>,
    },
    {
      title: '分块数',
      dataIndex: 'chunkCount',
      key: 'chunkCount',
      width: 90,
      render: (c: number) => <span style={{ color: colors.textMuted }}>{c}</span>,
    },
    {
      title: '入库时间',
      dataIndex: 'createdAt',
      key: 'createdAt',
      render: (d: string) => <span style={{ color: colors.textMuted }}>{d}</span>,
    },
  ];

  return (
    <div>
      <PageHeader
        title={kb?.name ?? '知识库详情'}
        subtitle="上传文档（.txt/.md/.csv/.json/.html/.pdf 等），系统自动提取文本、切分并向量入库"
        actions={
          <Space>
            <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/knowledge-bases')}>
              返回列表
            </Button>
            <Upload beforeUpload={handleUpload} showUploadList={false} accept=".txt,.md,.csv,.json,.html,.htm,.xml,.pdf">
              <Button type="primary" icon={<UploadOutlined />} loading={uploading}>
                上传文档
              </Button>
            </Upload>
          </Space>
        }
      />

      {loading ? (
        <Spin style={{ display: 'block', margin: '80px auto' }} />
      ) : (
        <>
          <Card title="知识库信息" style={{ marginBottom: 20 }}>
            <Descriptions column={2} size="small">
              <Descriptions.Item label="向量集合">
                <span style={{ fontFamily: "'IBM Plex Mono', monospace" }}>{kb?.collectionName}</span>
              </Descriptions.Item>
              <Descriptions.Item label="Embedding 模型">
                <Tag color="blue">{kb?.embeddingModel}</Tag>
              </Descriptions.Item>
              <Descriptions.Item label="描述" span={2}>
                {kb?.description || '-'}
              </Descriptions.Item>
            </Descriptions>
          </Card>

          <Card title={`文档列表（${documents.length}）`}>
            <Table columns={columns} dataSource={documents} rowKey="id" pagination={false} locale={{ emptyText: '暂无文档，点击右上角上传' }} />
          </Card>
        </>
      )}
    </div>
  );
};

export default KnowledgeBaseDetailPage;
