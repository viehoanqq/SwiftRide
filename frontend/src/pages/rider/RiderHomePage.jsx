import { Navigate } from 'react-router-dom';
import { checkTrip } from '../../auth/ProtectedRoute';
const trip = {
    id: 1,
}

export default function RiderHomePage() {
    return <Navigate to={checkTrip(trip)} replace />;
}