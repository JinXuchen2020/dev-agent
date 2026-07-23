import React from 'react';
import { Outlet, useNavigate, useLocation } from 'react-router-dom';
import { Layout, Menu, Button, theme } from 'antd';
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
} from '@ant-design/icons';
import { useAppStore } from '../stores/appStore';

const { Header, Sider, Content } = Layout;

const menuItems = [
  { key: '/', icon: <DashboardOutlined />, label: 'Dashboard' },
  { key: '/agents', icon: <RobotOutlined />, label: 'Agents' },
  { key: '/workflows', icon: <ApartmentOutlined />, label: 'Workflows' },
  { key: '/workflows/new', icon: <ApartmentOutlined />, label: 'Workflow Editor' },
  { key: '/agent-roles', icon: <TeamOutlined />, label: 'Agent Roles' },
  { key: '/agent-configurations', icon: <SettingOutlined />, label: 'Configurations' },
  { key: '/execution-logs', icon: <FileTextOutlined />, label: 'Execution Logs' },
  { key: '/knowledge-bases', icon: <BookOutlined />, label: '知识库' },
];

const AppLayout: React.FC = () => {
  const { sidebarCollapsed, toggleSidebar } = useAppStore();
  const navigate = useNavigate();
  const location = useLocation();
  const { token } = theme.useToken();

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
            borderBottom: `1px solid ${token.colorBorderSecondary}`,
          }}
        >
          <Button
            type="text"
            icon={sidebarCollapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
            onClick={toggleSidebar}
          />
        </Header>
        <Content style={{ margin: 24, minHeight: 280 }}>
          <Outlet />
        </Content>
      </Layout>
    </Layout>
  );
};

export default AppLayout;
