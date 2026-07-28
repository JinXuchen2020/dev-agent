import React, { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Table, Button, Spin, Space, Tag, Upload, Descriptions, App as AntApp } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { ArrowLeftOutlined, UploadOutlined } from '@ant-design/icons';
import type { KnowledgeDocument } from '../types';
import { getKnowledgeBase, uploadDocument, getErrorMessage } from '../services/api';
import PageHeader from '../components/PageHeader';
import Card from '../components/Card';
import { colors } from '../theme/tokens';
import { useTranslation } from 'react-i18next';

const KnowledgeBaseDetailPage: React.FC = () => {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const [kb, setKb] = useState<{ name: string; collectionName: string; embeddingModel: string; description: string } | null>(null);
  const [documents, setDocuments] = useState<KnowledgeDocument[]>([]);
  const [loading, setLoading] = useState(true);
  const [uploading, setUploading] = useState(false);
  const { message } = AntApp.useApp();
  const { t } = useTranslation();

  const load = () => {
    setLoading(true);
    getKnowledgeBase(id)
      .then((data) => {
        setKb({ name: data.name, collectionName: data.collectionName, embeddingModel: data.embeddingModel, description: data.description });
        setDocuments(data.documents);
      })
      .catch(() => message.error(t('pages.knowledgeBases.loadDetailFailed')))
      .finally(() => setLoading(false));
  };

  useEffect(load, [id]);

  const handleUpload = async (file: File) => {
    setUploading(true);
    try {
      await uploadDocument(id, file);
      message.success(t('pages.knowledgeBases.docIndexed', { name: file.name }));
      load();
    } catch (err) {
      message.error(getErrorMessage(err));
    } finally {
      setUploading(false);
    }
    return false; // 阻止 antd 自动上传
  };

  const columns: ColumnsType<KnowledgeDocument> = [
    {
      title: t('pages.knowledgeBases.colFileName'),
      dataIndex: 'fileName',
      key: 'fileName',
      render: (n: string) => <span style={{ color: colors.textPrimary, fontWeight: 500 }}>{n}</span>,
    },
    {
      title: t('pages.knowledgeBases.colType'),
      dataIndex: 'contentType',
      key: 'contentType',
      width: 160,
      render: (ct: string) => <Tag>{ct || 'text/plain'}</Tag>,
    },
    {
      title: t('pages.knowledgeBases.colChunks'),
      dataIndex: 'chunkCount',
      key: 'chunkCount',
      width: 90,
      render: (c: number) => <span style={{ color: colors.textMuted }}>{c}</span>,
    },
    {
      title: t('pages.knowledgeBases.colIndexedAt'),
      dataIndex: 'createdAt',
      key: 'createdAt',
      render: (d: string) => <span style={{ color: colors.textMuted }}>{d}</span>,
    },
  ];

  return (
    <div>
      <PageHeader
        title={kb?.name ?? t('pages.knowledgeBases.detailTitle')}
        subtitle={t('pages.knowledgeBases.uploadSubtitle')}
        actions={
          <Space>
            <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/knowledge-bases')}>
              {t('pages.knowledgeBases.backToList')}
            </Button>
            <Upload beforeUpload={handleUpload} showUploadList={false} accept=".txt,.md,.csv,.json,.html,.htm,.xml,.pdf">
              <Button type="primary" icon={<UploadOutlined />} loading={uploading}>
                {t('pages.knowledgeBases.uploadDoc')}
              </Button>
            </Upload>
          </Space>
        }
      />

      {loading ? (
        <Spin style={{ display: 'block', margin: '80px auto' }} />
      ) : (
        <>
          <Card title={t('pages.knowledgeBases.kbInfo')} style={{ marginBottom: 20 }}>
            <Descriptions column={2} size="small">
              <Descriptions.Item label={t('pages.knowledgeBases.vectorCollection')}>
                <span style={{ fontFamily: "'IBM Plex Mono', monospace" }}>{kb?.collectionName}</span>
              </Descriptions.Item>
              <Descriptions.Item label={t('pages.knowledgeBases.embeddingModel')}>
                <Tag color="blue">{kb?.embeddingModel}</Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('common.description')} span={2}>
                {kb?.description || '-'}
              </Descriptions.Item>
            </Descriptions>
          </Card>

          <Card title={t('pages.knowledgeBases.documentsCount', { count: documents.length })}>
            <Table columns={columns} dataSource={documents} rowKey="id" pagination={false} locale={{ emptyText: t('pages.knowledgeBases.emptyDocs') }} />
          </Card>
        </>
      )}
    </div>
  );
};

export default KnowledgeBaseDetailPage;
