import React from 'react';
import { Outlet, useNavigate, useLocation } from 'react-router-dom';
import { Layout, Menu, Button, theme, Dropdown, Avatar, App as AntApp } from 'antd';
import {
  DashboardOutlined,
  RobotOutlined,
  ApartmentOutlined,
  TeamOutlined,
  SettingOutlined,
  FileTextOutlined,
  MenuFoldOutlined,
  MenuUnfoldOutlined,
  BookOutlined,
  MessageOutlined,
  UserOutlined,
  LogoutOutlined,
  GlobalOutlined,
  KeyOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useAppStore } from '../stores/appStore';
import LanguageSwitcher from '../components/LanguageSwitcher';

const { Header, Sider, Content } = Layout;

const AppLayout: React.FC = () => {
  const { sidebarCollapsed, toggleSidebar, userEmail, userRole, logout } = useAppStore();
  const { message } = AntApp.useApp();
  const navigate = useNavigate();
  const location = useLocation();
  const { token } = theme.useToken();
  const { t } = useTranslation();

  // 凭据管理后端为 [Authorize(Roles="Admin,Operator")]，非该角色打开页面会 403，
  // 故侧边栏入口仅对 Admin / Operator 显示，避免无权用户看到报错页。
  const isAdminOrOperator =
    !!userRole && ['admin', 'operator'].includes(userRole.toLowerCase());

  const menuItems = [
    { key: '/', icon: <DashboardOutlined />, label: t('nav.dashboard') },
    { key: '/agents', icon: <RobotOutlined />, label: t('nav.agents') },
    { key: '/workflows', icon: <ApartmentOutlined />, label: t('nav.workflows') },
    { key: '/workflows/new', icon: <ApartmentOutlined />, label: t('nav.workflowEditor') },
    { key: '/agent-roles', icon: <TeamOutlined />, label: t('nav.agentRoles') },
    { key: '/agent-configurations', icon: <SettingOutlined />, label: t('nav.configurations') },
    { key: '/credentials', icon: <KeyOutlined />, label: t('nav.credentials') },
    { key: '/execution-logs', icon: <FileTextOutlined />, label: t('nav.executionLogs') },
    { key: '/knowledge-bases', icon: <BookOutlined />, label: t('nav.knowledgeBases') },
    { key: '/conversations', icon: <MessageOutlined />, label: t('nav.conversations') },
    { key: '/research', icon: <GlobalOutlined />, label: t('nav.research') },
  ];
  if (!isAdminOrOperator) {
    // 仅 Admin/Operator 可见「我的凭据」（与后端 RBAC 对齐）。
    const idx = menuItems.findIndex((m) => m.key === '/credentials');
    if (idx >= 0) menuItems.splice(idx, 1);
  }

  const handleLogout = (): void => {
    logout();
    message.success(t('layout.logoutSuccess'));
    navigate('/login');
  };

  const userMenu = {
    items: [{ key: 'logout', icon: <LogoutOutlined />, label: t('nav.logout') }],
    onClick: ({ key }: { key: string }): void => {
      if (key === 'logout') handleLogout();
    },
  };

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider
        trigger={null}
        collapsible
        collapsed={sidebarCollapsed}
        style={{
          overflow: 'auto',
          height: '100vh',
          position: 'fixed',
          left: 0,
          top: 0,
          bottom: 0,
          background: token.colorBgContainer,
          borderRight: `1px solid ${token.colorBorderSecondary}`,
        }}
      >
        <div
          style={{
            height: 64,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontWeight: 700,
            fontSize: sidebarCollapsed ? 14 : 18,
            color: token.colorPrimary,
            borderBottom: `1px solid ${token.colorBorderSecondary}`,
          }}
        >
          {sidebarCollapsed ? 'AP' : 'Agent Platform'}
        </div>
        <Menu
          mode="inline"
          selectedKeys={[location.pathname]}
          items={menuItems}
          onClick={({ key }) => navigate(key)}
          style={{ borderInlineEnd: 'none' }}
        />
      </Sider>
      <Layout style={{ marginLeft: sidebarCollapsed ? 80 : 200, transition: 'margin-left 0.2s' }}>
        <Header
          style={{
            padding: '0 24px',
            background: token.colorBgContainer,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            borderBottom: `1px solid ${token.colorBorderSecondary}`,
          }}
        >
          <Button
            type="text"
            aria-label={sidebarCollapsed ? t('nav.expandSidebar') : t('nav.collapseSidebar')}
            icon={sidebarCollapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
            onClick={toggleSidebar}
          />
          <LanguageSwitcher />
          <Dropdown menu={userMenu} placement="bottomRight">
            <Button type="text" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <Avatar size="small" icon={<UserOutlined />} />
              <span>{userEmail ?? t('nav.notLoggedIn')}</span>
            </Button>
          </Dropdown>
        </Header>
        <Content style={{ margin: 24, minHeight: 280 }}>
          <Outlet />
        </Content>
      </Layout>
    </Layout>
  );
};

export default AppLayout;
