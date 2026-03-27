CREATE TABLE users (
  id UUID PRIMARY KEY,
  display_name TEXT NOT NULL CHECK (char_length(display_name) >= 1),
  created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_users_display_name ON users(display_name);

CREATE TABLE anime (
  id BIGSERIAL PRIMARY KEY,
  provider TEXT NOT NULL CHECK (char_length(provider) >= 1),
  provider_id TEXT NOT NULL CHECK (char_length(provider_id) >= 1),
  title TEXT NOT NULL CHECK (char_length(title) >= 1),
  genres TEXT[] NOT NULL DEFAULT '{}',
  episodes INT CHECK (episodes IS NULL OR episodes >= 0),
  year INT CHECK (year IS NULL OR year >= 1900),
  trailer_youtube_id TEXT,
  created_at TIMESTAMP NOT NULL DEFAULT NOW(),
  UNIQUE(provider, provider_id)
);

CREATE INDEX idx_anime_title ON anime(title);
CREATE INDEX idx_anime_year ON anime(year);
CREATE INDEX idx_anime_provider_provider_id ON anime(provider, provider_id);

CREATE TABLE quests (
  id BIGSERIAL PRIMARY KEY,
  code TEXT NOT NULL UNIQUE CHECK (char_length(code) >= 1),
  title TEXT NOT NULL CHECK (char_length(title) >= 1),
  description TEXT NOT NULL,
  requirements JSONB NOT NULL DEFAULT '{}'::jsonb,
  rewards JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_quests_code ON quests(code);

CREATE TABLE user_quests (
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  quest_id BIGINT NOT NULL REFERENCES quests(id) ON DELETE CASCADE,
  status TEXT NOT NULL CHECK (status IN ('active', 'completed', 'failed')),
  progress JSONB NOT NULL DEFAULT '{}'::jsonb,
  updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
  PRIMARY KEY (user_id, quest_id)
);

CREATE INDEX idx_user_quests_status ON user_quests(status);
CREATE INDEX idx_user_quests_updated_at ON user_quests(updated_at);

CREATE TABLE watch_entries (
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  anime_id BIGINT NOT NULL REFERENCES anime(id) ON DELETE CASCADE,
  status TEXT NOT NULL CHECK (status IN ('watching', 'completed', 'planned', 'dropped', 'on_hold')),
  score INT CHECK (score IS NULL OR (score >= 0 AND score <= 10)),
  episodes_watched INT NOT NULL DEFAULT 0 CHECK (episodes_watched >= 0),
  updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
  PRIMARY KEY (user_id, anime_id)
);

CREATE INDEX idx_watch_entries_status ON watch_entries(status);
CREATE INDEX idx_watch_entries_updated_at ON watch_entries(updated_at);

CREATE TABLE achievements (
  id BIGSERIAL PRIMARY KEY,
  code TEXT NOT NULL UNIQUE CHECK (char_length(code) >= 1),
  title TEXT NOT NULL CHECK (char_length(title) >= 1),
  description TEXT NOT NULL,
  category TEXT NOT NULL CHECK (char_length(category) >= 1),
  icon TEXT,
  requirements JSONB NOT NULL DEFAULT '{}'::jsonb,
  points INT NOT NULL DEFAULT 0 CHECK (points >= 0),
  is_hidden BOOLEAN NOT NULL DEFAULT FALSE,
  created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_achievements_code ON achievements(code);
CREATE INDEX idx_achievements_category ON achievements(category);

CREATE TABLE user_achievements (
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  achievement_id BIGINT NOT NULL REFERENCES achievements(id) ON DELETE CASCADE,
  status TEXT NOT NULL DEFAULT 'locked' CHECK (status IN ('locked', 'in_progress', 'unlocked')),
  progress JSONB NOT NULL DEFAULT '{}'::jsonb,
  unlocked_at TIMESTAMP,
  updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
  PRIMARY KEY (user_id, achievement_id)
);

CREATE INDEX idx_user_achievements_status ON user_achievements(status);
CREATE INDEX idx_user_achievements_unlocked_at ON user_achievements(unlocked_at);
