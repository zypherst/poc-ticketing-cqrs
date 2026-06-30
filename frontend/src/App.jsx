import React, { useState, useEffect } from 'react';

export default function App() {
  // Navigation & Data States
  const [view, setView] = useState('home'); // 'home' | 'layout' | 'payment'
  const [branches, setBranches] = useState([]);
  const [selectedBranch, setSelectedBranch] = useState('');
  const [showtimes, setShowtimes] = useState([]);
  const [selectedShowtime, setSelectedShowtime] = useState(null);
  
  // Seat Management States
  const [seatPlan, setSeatPlan] = useState([]);
  const [selectedSeats, setSelectedSeats] = useState([]); // เก้าอี้ที่ผู้ใช้คลิกเลือกในเครื่องปัจจุบัน
  const [dataSource, setDataSource] = useState('');
  
  // Requirement 13: แถบ Console ด้านขวา
  const [logs, setLogs] = useState([]);

  const API_URL = `http://${window.location.hostname}:5000/api/ticket`;

  const pushLog = (message, timestamp = new Date().toLocaleTimeString()) => {
    setLogs(prev => [{ timestamp, message }, ...prev]);
  };

  // ดึงข้อมูลสาขาตอนเริ่มต้น
  useEffect(() => {
    fetch(`${API_URL}/branches`)
      .then(res => res.json())
      .then(data => setBranches(data))
      .catch(() => pushLog("❌ ไม่สามารถเชื่อมต่อ API สำหรับโหลดข้อมูลสาขาได้"));
  }, []);

  // เมื่อเลือกสาขา ให้โหลดรอบฉาย
  useEffect(() => {
    if (selectedBranch) {
      fetch(`${API_URL}/showtimes/${selectedBranch}`)
        .then(res => res.json())
        .then(data => {
          setShowtimes(data);
          pushLog(`📅 โหลดข้อมูลรอบฉายสาขาที่ ${selectedBranch} สำเร็จ`);
        });
    }
  }, [selectedBranch]);

  // Requirement 9, 14: ดึงผังที่นั่ง
  const fetchSeatPlan = async (showtime) => {
    setSelectedShowtime(showtime);
    try {
      const res = await fetch(`${API_URL}/seats/${showtime.id}`);
      const result = await res.json();
      
      setSeatPlan(result.data.seats);
      setDataSource(result.source);
      
      // เอา Log จากหลังบ้านมาแสดงที่ฝั่งหน้าบ้านด้วย
      if (result.logs) {
        result.logs.forEach(l => pushLog(l.message, l.timestamp));
      }
      setView('layout');
    } catch (err) {
      pushLog("❌ เกิดข้อผิดพลาดในการโหลดผังที่นั่ง");
    }
  };

  // จัดการการคลิกเลือกเก้าอี้บน Grid Layout
  const handleSeatClick = (seat) => {
    if (seat.status === 'Lock' || seat.status === 'Paid') return; // ห้ามเลือก

    if (selectedSeats.includes(seat.seatCode)) {
      setSelectedSeats(prev => prev.filter(code => code !== seat.seatCode));
      pushLog(`🖱️ ผู้ใช้ยกเลิกการเลือกที่นั่ง ${seat.seatCode}`);
    } else {
      setSelectedSeats(prev => [...prev, seat.seatCode]);
      pushLog(`🖱️ ผู้ใช้กดเลือกที่นั่ง ${seat.seatCode}`);
    }
  };

  // Requirement 7: กดไปหน้าจ่ายเงิน (เปลี่ยนสถานะเก้าอี้เป็น Lock ทั้งคู่)
  const proceedToPayment = async () => {
    if (selectedSeats.length === 0) {
      alert("กรุณาเลือกที่นั่งก่อนครับ!");
      return;
    }

    pushLog(`⏳ กำลังทำการ Lock ที่นั่งจำนวน ${selectedSeats.length} ที่นั่ง...`);
    const res = await fetch(`${API_URL}/seats/update-status`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        showtimeId: selectedShowtime.id,
        seatCodes: selectedSeats,
        status: 'Lock'
      })
    });
    const result = await res.json();
    result.logs.forEach(l => pushLog(l.message, l.timestamp));
    
    setView('payment');
  };

  // Requirement 8: กดกลับจากหน้าจ่ายเงิน ปลดล๊อกในระบบ แต่ Frontend ยังติ๊กเก้าอี้ตัวเดิมไว้ให้
  const backToLayout = async () => {
    pushLog(`🔄 ผู้ใช้กดกลับ: กำลังปลด Lock เก้าอี้ให้กลับมาเป็นสถานะว่าง...`);
    const res = await fetch(`${API_URL}/seats/update-status`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        showtimeId: selectedShowtime.id,
        seatCodes: selectedSeats,
        status: ''
      })
    });
    const result = await res.json();
    result.logs.forEach(l => pushLog(l.message, l.timestamp));

    // โหลดผังล่าสุดมาวาดใหม่
    const freshRes = await fetch(`${API_URL}/seats/${selectedShowtime.id}`);
    const freshResult = await freshRes.json();
    setSeatPlan(freshResult.data.seats);
    
    setView('layout');
  };

  // Requirement 11, 12: ชำระเงินสำเร็จ เปลี่ยนเป็น Paid บันทึกเวลาลง DB และกลับหน้าแรก
  const handlePaymentSubmit = async () => {
    pushLog(`💸 ผู้ใช้กดชำระเงิน: กำลังบันทึกสถานะการจ่ายเงิน...`);
    const res = await fetch(`${API_URL}/seats/update-status`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        showtimeId: selectedShowtime.id,
        seatCodes: selectedSeats,
        status: 'Paid'
      })
    });
    const result = await res.json();
    result.logs.forEach(l => pushLog(l.message, l.timestamp));

    alert("ชำระเงินสำเร็จเรียบร้อยแล้ว!");
    // ล้างข้อมูลและกลับสู่หน้าหลัก
    setSelectedSeats([]);
    setSelectedShowtime(null);
    setView('home');
  };

  // ฟังก์ชันจัดกลุ่มเก้าอี้แยกตามแถว A-G เพื่อแสดงผลแบบ Matrix Grid
const renderSeatGrid = () => {
    const rows = ['A', 'B', 'C', 'D', 'E', 'F', 'G'];
    return rows.map(rowLetter => {
      // 🛠️ แก้ไข: กรองเฉพาะแถว แล้วแยกตัวเลขออกมาเรียงลำดับ (Sort) ให้ถูกต้อง
      const seatsInRow = seatPlan
        .filter(s => s.seatCode.startsWith(rowLetter))
        .sort((a, b) => {
          // ตัดตัวอักษรตัวแรกออก (เช่น 'A') แล้วแปลงที่เหลือเป็นตัวเลข (Int) เพื่อเปรียบเทียบ
          const numA = parseInt(a.seatCode.substring(1), 10);
          const numB = parseInt(b.seatCode.substring(1), 10);
          return numA - numB;
        });

      return (
        <div key={rowLetter} className="flex items-center gap-2 mb-2 justify-center">
          <div className="w-6 font-bold text-gray-400">{rowLetter}</div>
          {seatsInRow.map(seat => {
            let seatColor = "bg-green-500 hover:bg-green-600 text-white"; // ว่าง = ""
            if (seat.status === 'Lock') seatColor = "bg-blue-600 text-white cursor-not-allowed";
            if (seat.status === 'Paid') seatColor = "bg-red-600 text-white cursor-not-allowed";
            if (selectedSeats.includes(seat.seatCode)) seatColor = "bg-yellow-500 text-black font-semibold ring-2 ring-black";

            return (
              <button
                key={seat.seatCode}
                onClick={() => handleSeatClick(seat)}
                disabled={seat.status === 'Lock' || seat.status === 'Paid'}
                className={`w-10 h-10 rounded text-xs transition-colors duration-200 ${seatColor}`}
                title={`ที่นั่ง: ${seat.seatCode} สถานะ: ${seat.status || 'ว่าง'}`}
              >
                {seat.seatCode.replace(rowLetter, '')}
              </button>
            );
          })}
        </div>
      );
    });
  };

  return (
    <div className="min-h-screen bg-gray-900 text-white flex">
      {/* ฝั่งซ้าย: เนื้อหาระบบทดสอบหลัก (75% Width) */}
      <div className="w-9/12 p-8 overflow-y-auto">
        <h1 className="text-3xl font-extrabold text-blue-400 mb-6">🎬 Cinema Ticket Booking PoC Engine</h1>

        {/* ---------------- หน้า 1: เลือกสาขาและรอบฉาย ---------------- */}
        {view === 'home' && (
          <div className="bg-gray-800 p-6 rounded-lg shadow-lg">
            <h2 className="text-xl font-bold mb-4 text-gray-200">ขั้นตอนที่ 1: เลือกสาขาโรงภาพยนตร์</h2>
            <select 
              className="w-full p-3 bg-gray-700 rounded text-white border border-gray-600 mb-6"
              value={selectedBranch}
              onChange={(e) => setSelectedBranch(e.target.value)}
            >
              <option value="">-- เลือกสาขาภาพยนตร์ --</option>
              {branches.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}
            </select>

            {selectedBranch && (
              <div>
                <h3 className="text-lg font-semibold mb-3 text-blue-300">ตารางรอบฉายทั้งหมดของสาขานี้:</h3>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  {showtimes.map(st => (
                    <div key={st.id} className="bg-gray-700 p-4 rounded-lg border border-gray-600 hover:border-blue-400 transition-all">
                      <div className="font-bold text-lg text-white">{st.movieTitle}</div>
                      <div className="text-sm text-gray-300 mt-1">🏟️ {st.cinemaName}</div>
                      <div className="text-sm text-yellow-400 mt-1">⏰ เวลา: {new Date(st.showTime).toLocaleString('th-TH')}</div>
                      <button
                        onClick={() => fetchSeatPlan(st)}
                        className="mt-3 w-full bg-blue-500 hover:bg-blue-600 text-white py-2 rounded font-medium text-sm transition-colors"
                      >
                        เลือกรอบฉายนี้
                      </button>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        )}

        {/* ---------------- หน้า 2: หน้าแสดงผังโรงหนัง ---------------- */}
        {view === 'layout' && selectedShowtime && (
          <div className="bg-gray-800 p-6 rounded-lg shadow-lg">
            <div className="flex justify-between items-center mb-4">
              <h2 className="text-xl font-bold text-gray-200">ขั้นตอนที่ 2: เลือกที่นั่งสำหรับรอบฉาย</h2>
              <span className={`px-3 py-1 rounded text-sm font-semibold ${dataSource === 'NATS' ? 'bg-purple-600' : 'bg-orange-600'}`}>
                ดึงข้อมูลจากแหล่ง: {dataSource}
              </span>
            </div>
            
            <div className="mb-6 p-3 bg-gray-700 rounded border-l-4 border-blue-500">
              <p className="font-semibold text-white">{selectedShowtime.movieTitle}</p>
              <p className="text-xs text-gray-300">{selectedShowtime.cinemaName} | {new Date(selectedShowtime.showTime).toLocaleString('th-TH')}</p>
            </div>

            {/* หน้าจอแสดงจอภาพยนตร์หลอกๆ */}
            <div className="w-full bg-gray-600 text-center text-xs py-1 rounded-sm text-gray-300 font-bold tracking-widest mb-10 shadow-inner">
              SCREEN (จอภาพยนตร์)
            </div>

            {/* แสดงตารางเก้าอี้ */}
            <div className="overflow-x-auto p-4 bg-gray-850 rounded-lg border border-gray-700 mb-6">
              {renderSeatGrid()}
            </div>

            {/* ความหมายของสีสถานะต่างๆ */}
            <div className="flex justify-center gap-6 mb-6 text-sm">
              <span className="flex items-center gap-2"><div className="w-4 h-4 bg-green-500 rounded"></div>ว่าง</span>
              <span className="flex items-center gap-2"><div className="w-4 h-4 bg-yellow-500 rounded"></div>กำลังเลือก</span>
              <span className="flex items-center gap-2"><div className="w-4 h-4 bg-blue-600 rounded"></div>โดนล๊อค (Lock)</span>
              <span className="flex items-center gap-2"><div className="w-4 h-4 bg-red-600 rounded"></div>ซื้อแล้ว (Paid)</span>
            </div>

            <div className="flex justify-between mt-6">
              <button onClick={() => setView('home')} className="bg-gray-600 hover:bg-gray-700 text-white px-6 py-2 rounded font-semibold transition-colors">
                🔙 ย้อนกลับ
              </button>
              <button onClick={proceedToPayment} className="bg-blue-500 hover:bg-blue-600 text-white px-8 py-2 rounded font-bold transition-colors">
                🔒 ยืนยันเพื่อไปหน้าชำระเงิน
              </button>
            </div>
          </div>
        )}

        {/* ---------------- หน้า 3: หน้าชำระเงินหลอก ---------------- */}
        {view === 'payment' && (
          <div className="bg-gray-800 p-6 rounded-lg shadow-lg text-center max-w-xl mx-auto">
            <h2 className="text-2xl font-bold text-yellow-400 mb-4">💳 หน้าต่างชำระเงิน (Mockup Payment Portal)</h2>
            <p className="text-gray-300 mb-6">กรุณาตรวจสอบรายการสั่งซื้อของคุณก่อนดำเนินการจ่ายเงิน</p>

            <div className="bg-gray-700 p-4 rounded border border-gray-600 text-left mb-6">
              <p className="text-sm text-gray-400">ภาพยนตร์:</p>
              <p className="font-bold text-lg mb-2 text-white">{selectedShowtime.movieTitle}</p>
              <p className="text-sm text-gray-400">เก้าอี้ที่เลือกจองไว้:</p>
              <p className="font-mono text-xl text-green-400 font-bold">{selectedSeats.join(', ')}</p>
            </div>

            <div className="flex gap-4 justify-center">
              <button onClick={backToLayout} className="bg-red-500 hover:bg-red-600 text-white px-6 py-3 rounded font-bold transition-colors">
                ❌ ยกเลิกและย้อนกลับ
              </button>
              <button onClick={handlePaymentSubmit} className="bg-green-500 hover:bg-green-600 text-gray-900 px-8 py-3 rounded font-black text-lg transition-colors shadow-lg">
                💵 กดปุ่มจ่ายเงินหลอกๆ (Confirm Paid)
              </button>
            </div>
          </div>
        )}
      </div>

      {/* ---------------- ส่วนที่ 13: แถบ Console ด้านขวาแสดง Realtime Actions ---------------- */}
      <div className="w-3/12 bg-gray-950 border-l border-gray-800 flex flex-col p-4">
        <div className="font-black text-sm tracking-widest text-red-400 mb-2 pb-2 border-b border-gray-800 flex items-center gap-2">
          <span>📟 SYSTEM TRACE CONSOLE</span>
          <span className="w-2 h-2 rounded-full bg-red-500 animate-pulse"></span>
        </div>
        <div className="flex-1 overflow-y-auto font-mono text-xs space-y-2 select-text">
          {logs.length === 0 ? (
            <p className="text-gray-600 italic">No events recorded yet. Perform actions to trace NATS/DB flow.</p>
          ) : (
            logs.map((log, index) => (
              <div key={index} className="p-2 bg-gray-900 rounded border border-gray-800 animate-fadeIn">
                <span className="text-gray-500 font-bold pr-1">[{log.timestamp}]</span>
                <span className="text-gray-300 leading-relaxed">{log.message}</span>
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
}