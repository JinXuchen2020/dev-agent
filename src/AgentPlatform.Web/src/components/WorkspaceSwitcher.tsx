// F35 · 顶栏工作空间切换器。切换调用后端 /switch（校验可见性 + 重签 cookie），
// 更新 appStore.currentWorkspaceId —— useApiState 内部订阅该值，全站数据自动刷新（决策 D5=A）。
import { useState } from 'react';
import { Button, Drawer, Dropdown, Form, Input, Modal, Select, Space, Table, App as AntApp } from 'antd';
import { DownOutlined, PlusOutlined, SettingOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useAppStore } from '../stores/appStore';
import { useApiState } from '../hooks/useApiState';
import {
  getWorkspaces,
  createWorkspace,
  updateWorkspace,
  deleteWorkspace,
  getWorkspaceMembers,
  addWorkspaceMember,
  removeWorkspaceMember,
  switchWorkspace,
} from '../services/api';
import type { Workspace, WorkspaceMember } from '../types';

const WorkspaceSwitcher: React.FC = () => {
  const { t } = useTranslation();
  const { message, modal } = AntApp.useApp();
  const { userRole, isAuthenticated, isDemo, currentWorkspaceId, setCurrentWorkspaceId } = useAppStore();
  const isAdmin = !!userRole && userRole.toLowerCase() === 'admin';

  const [switching, setSwitching] = useState(false);
  const [manageOpen, setManageOpen] = useState(false);
  const [editing, setEditing] = useState<Workspace | null>(null); // null=新建
  const [form] = Form.useForm<{ name: string; description?: string }>();
  const [saving, setSaving] = useState(false);

  // 成员管理（针对选中待管理的目标工作空间）
  const [manageTarget, setManageTarget] = useState<Workspace | null>(null);
  const [members, setMembers] = useState<WorkspaceMember[]>([]);
  const [membersLoading, setMembersLoading] = useState(false);
  const [memberEmail, setMemberEmail] = useState('');
  const [addingMember, setAddingMember] = useState(false);

  // 工作空间列表：普通 useApiState（切换 workspace 时重拉无害，且能反映新建/删除结果）。
  const { data: workspaces, retry: reloadWorkspaces } = useApiState(() => getWorkspaces(), []);

  if (!isAuthenticated || isDemo) return null;

  const options = (workspaces ?? []).map((w) => ({
    value: w.id,
    label: w.isDefault ? `${w.name} · ${t('workspace.default')}` : w.name,
  }));

  const currentName =
    (workspaces ?? []).find((w) => w.id === currentWorkspaceId)?.name ?? t('workspace.label');

  const handleChange = async (id: string) => {
    if (id === currentWorkspaceId) return;
    setSwitching(true);
    try {
      await switchWorkspace(id);
      setCurrentWorkspaceId(id);
      const name = (workspaces ?? []).find((w) => w.id === id)?.name;
      message.success(t('workspace.switchSuccess', { name: name ?? '' }));
    } catch {
      message.error(t('workspace.switchFailed'));
    } finally {
      setSwitching(false);
    }
  };

  const openCreate = () => {
    setEditing(null);
    form.setFieldsValue({ name: '', description: '' });
    setManageOpen(true);
  };

  const openEdit = () => {
    const current = (workspaces ?? []).find((w) => w.id === currentWorkspaceId);
    if (!current) return;
    setEditing(current);
    form.setFieldsValue({ name: current.name, description: current.description ?? '' });
    setManageOpen(true);
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      setSaving(true);
      if (editing) {
        await updateWorkspace(editing.id, { name: values.name, description: values.description || null });
        message.success(t('workspace.updateSuccess'));
      } else {
        await createWorkspace({ name: values.name, description: values.description || null });
        message.success(t('workspace.createSuccess'));
      }
      setManageOpen(false);
      reloadWorkspaces();
    } catch {
      // validateFields 校验失败或后端错误：后者给出统一提示。
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = () => {
    const current = (workspaces ?? []).find((w) => w.id === currentWorkspaceId);
    if (!current) return;
    modal.confirm({
      title: t('workspace.delete'),
      content: t('workspace.deleteConfirm', { name: current.name }),
      okType: 'danger',
      onOk: async () => {
        try {
          await deleteWorkspace(current.id);
          message.success(t('workspace.deleteSuccess'));
          if (currentWorkspaceId === current.id) {
            // 删除的是当前工作空间：cookie 里的 workspace_id claim 仍指向已删空间，
            // 若仅清空本地状态，请求会回落到失效 claim → 全站查询为空。
            // 切回默认工作空间（默认空间恒存在且不可删），保持 claim 与本地状态一致。
            const fallback = (workspaces ?? []).find((w) => w.isDefault && w.id !== current.id);
            if (fallback) {
              try {
                await switchWorkspace(fallback.id);
                setCurrentWorkspaceId(fallback.id);
              } catch {
                setCurrentWorkspaceId(null);
              }
            } else {
              setCurrentWorkspaceId(null);
            }
          }
          reloadWorkspaces();
        } catch {
          // 409（默认空间 / 非空）等：后端 ProblemDetails 文案已含原因，交由全局错误提示兜底。
          message.error(t('workspace.deleteFailed'));
        }
      },
    });
  };

  const openMembers = async () => {
    const current = (workspaces ?? []).find((w) => w.id === currentWorkspaceId);
    if (!current) return;
    setManageTarget(current);
    setMembersLoading(true);
    try {
      setMembers(await getWorkspaceMembers(current.id));
    } catch {
      setMembers([]);
    } finally {
      setMembersLoading(false);
    }
  };

  const handleAddMember = async () => {
    if (!manageTarget || !memberEmail.trim()) return;
    setAddingMember(true);
    try {
      const member = await addWorkspaceMember(manageTarget.id, { email: memberEmail.trim() });
      setMembers((prev) => [...prev, member]);
      setMemberEmail('');
      message.success(t('workspace.memberAdded'));
    } catch {
      message.error(t('workspace.memberAddFailed'));
    } finally {
      setAddingMember(false);
    }
  };

  const handleRemoveMember = async (userId: string) => {
    if (!manageTarget) return;
    try {
      await removeWorkspaceMember(manageTarget.id, userId);
      setMembers((prev) => prev.filter((m) => m.userId !== userId));
      message.success(t('workspace.memberRemoved'));
    } catch {
      message.error(t('workspace.memberRemoveFailed'));
    }
  };

  const manageMenu = {
    items: [
      { key: 'create', icon: <PlusOutlined />, label: t('workspace.create') },
      { key: 'edit', label: t('workspace.edit') },
      { key: 'members', label: t('workspace.manageMembers') },
      { key: 'delete', danger: true, label: t('workspace.delete') },
    ],
    onClick: ({ key }: { key: string }): void => {
      if (key === 'create') void openCreate();
      if (key === 'edit') openEdit();
      if (key === 'members') void openMembers();
      if (key === 'delete') handleDelete();
    },
  };

  return (
    <Space size="small" aria-label={t('workspace.label')}>
      <Select
        style={{ minWidth: 140 }}
        aria-label={t('workspace.label')}
        value={currentWorkspaceId ?? undefined}
        placeholder={currentName}
        options={options}
        loading={switching}
        onChange={(v) => void handleChange(v)}
      />
      {isAdmin && (
        <Dropdown menu={manageMenu} placement="bottomRight">
          <Button type="text" aria-label={t('workspace.manage')} icon={<SettingOutlined />}>
            <DownOutlined />
          </Button>
        </Dropdown>
      )}

      <Modal
        title={editing ? t('workspace.edit') : t('workspace.create')}
        open={manageOpen}
        onOk={() => void handleSave()}
        confirmLoading={saving}
        onCancel={() => setManageOpen(false)}
        destroyOnClose
      >
        <Form form={form} layout="vertical">
          <Form.Item
            name="name"
            label={t('workspace.name')}
            rules={[{ required: true, message: t('common.required') }]}
          >
            <Input maxLength={100} />
          </Form.Item>
          <Form.Item name="description" label={t('common.description')}>
            <Input.TextArea maxLength={500} rows={2} />
          </Form.Item>
        </Form>
      </Modal>

      <Drawer
        title={t('workspace.manageMembers')}
        open={manageTarget !== null}
        onClose={() => setManageTarget(null)}
        width={420}
      >
        <Space.Compact style={{ width: '100%', marginBottom: 16 }}>
          <Input
            placeholder={t('workspace.memberEmail')}
            value={memberEmail}
            onChange={(e) => setMemberEmail(e.target.value)}
            onPressEnter={() => void handleAddMember()}
          />
          <Button type="primary" loading={addingMember} onClick={() => void handleAddMember()}>
            {t('workspace.addMember')}
          </Button>
        </Space.Compact>
        <Table
          rowKey="userId"
          size="small"
          loading={membersLoading}
          dataSource={members}
          pagination={false}
          columns={[
            { title: t('workspace.memberEmail'), dataIndex: 'email' },
            {
              title: t('common.operation'),
              width: 80,
              render: (_, record) => (
                <Button type="link" danger size="small" onClick={() => void handleRemoveMember(record.userId)}>
                  {t('workspace.removeMember')}
                </Button>
              ),
            },
          ]}
        />
      </Drawer>
    </Space>
  );
};

export default WorkspaceSwitcher;
