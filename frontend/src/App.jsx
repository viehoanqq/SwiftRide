import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { AuthProvider } from './auth/AuthContext';
import ProtectedRoute from './auth/ProtectedRoute';
import LoginPage from './pages/auth/LoginPage';
import RegisterPage from './pages/auth/RegisterPage';
import HomePage from './pages/HomePage';
import RiderHomePage from './pages/Rider/RiderHomePage';
import BookPage from './pages/Rider/BookPage';
import TripPage from './pages/Rider/TripPage';

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/register" element={<RegisterPage />} />
          <Route path="/rider" element={<ProtectedRoute role="Rider"><RiderHomePage /></ProtectedRoute>} />
          <Route path="/rider/book" element={<ProtectedRoute role="Rider"><BookPage /></ProtectedRoute>} />
          <Route path="/rider/trips/:id" element={<ProtectedRoute role="Rider"><TripPage /></ProtectedRoute>} />
          <Route path="/driver" element={<ProtectedRoute role="Driver"><HomePage /></ProtectedRoute>} />
          <Route path="*" element={<Navigate to="/login" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}
