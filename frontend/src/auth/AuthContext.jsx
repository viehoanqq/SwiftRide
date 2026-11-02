import { createContext, useContext, useState } from 'react';
import { jwtDecode } from 'jwt-decode';
import { authApi } from '../api/authApi';
import { tokenStorage } from '../api/client';

const AuthContext = createContext(null);

// Đọc thông tin người dùng từ token đang lưu (chỉ để hiển thị, không phải kiểm tra bảo mật)
function readUserFromToken() {
  const token = tokenStorage.get();
  if (!token) return null;
  try {
    const { sub, name, role, exp } = jwtDecode(token);
    if (exp * 1000 < Date.now()) {          // token đã hết hạn
      tokenStorage.clear();
      return null;
    }
    return { id: sub, name, role };
  } catch {
    tokenStorage.clear();                   // token hỏng
    return null;
  }
}

export function AuthProvider({ children }) {
  const [user, setUser] = useState(readUserFromToken);

  const login = async (phone, password) => {
    const result = await authApi.login(phone, password);
    tokenStorage.set(result.accessToken);     // ← LƯU TOKEN
    const current = readUserFromToken();
    setUser(current);
    return current;
  };

  const logout = () => {
    tokenStorage.clear();
    setUser(null);
  };

  return <AuthContext.Provider value={{ user, login, logout }}>{children}</AuthContext.Provider>;
}

export const useAuth = () => useContext(AuthContext);
